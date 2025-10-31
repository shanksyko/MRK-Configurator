using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Mieruka.Core.Contracts;
using Serilog;

using UITimer = System.Windows.Forms.Timer;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Coordinates layout suspension and binding batches when the editor tab is reconfigured.
/// </summary>
internal sealed class TabEditCoordinator
{
    private const int ResumeDebounceIntervalMilliseconds = 150;
    private const int UiEventDebounceIntervalMilliseconds = 120;

    private readonly Control _root;
    private readonly IBindingService? _bindingService;
    private readonly Action? _pausePreview;
    private readonly Action? _resumePreview;
    private readonly ILogger _logger;
    private readonly UITimer _resumeDebounceTimer;
    private readonly object _debounceGate = new();
    private readonly UITimer _uiEventDebounceTimer;
    private readonly object _uiEventDebounceGate = new();
    private readonly MethodInfo? _applyAppTypeUiMethod;

    private int _applying;
    private IDisposable? _bindingScope;
    private string? _pendingResumeContext;
    private bool _debounceTimerDisposed;
    private bool _uiEventDebounceTimerDisposed;
    private bool _uiEventHandlersAttached;

    public TabEditCoordinator(
        Control root,
        IBindingService? bindingService,
        Action? pausePreview,
        Action? resumePreview,
        ILogger? logger = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _bindingService = bindingService;
        _pausePreview = pausePreview;
        _resumePreview = resumePreview;
        _logger = (logger ?? Log.ForContext<TabEditCoordinator>())
            .ForContext("RootControl", root.Name);

        _resumeDebounceTimer = new UITimer
        {
            Interval = ResumeDebounceIntervalMilliseconds,
        };
        _resumeDebounceTimer.Tick += ResumeDebounceTimerOnTick;

        _uiEventDebounceTimer = new UITimer
        {
            Interval = UiEventDebounceIntervalMilliseconds,
        };
        _uiEventDebounceTimer.Tick += OnUiEventDebounceTimerTick;

        _applyAppTypeUiMethod = root.GetType().GetMethod(
            "ApplyAppTypeUI",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        AttachUiEventHandlers();

        if (_root.IsDisposed)
        {
            DisposeTimers();
        }
        else
        {
            _root.Disposed += (_, _) => DisposeTimers();
        }
    }

    /// <summary>
    /// Begins a guarded edit scope.
    /// </summary>
    public EditScope BeginEditScope(string? context = null)
    {
        CancelPendingResume();

        if (Interlocked.Exchange(ref _applying, 1) == 1)
        {
            _logger.Debug("ReentrancyBlocked {Context}", context);
            return new EditScope(null, context, false);
        }

        _logger.Information("EnterEditTab {Context}", context);

        try
        {
            _root.SuspendLayout();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "LayoutSuspendFailed {Context}", context);
        }

        _bindingScope = _bindingService?.BeginBatch();

        try
        {
            _pausePreview?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "PreviewPauseFailed {Context}", context);
        }

        return new EditScope(this, context, true);
    }

    private void EndEditScope(string? context)
    {
        try
        {
            _bindingScope?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "BindingBatchDisposeFailed {Context}", context);
        }
        finally
        {
            _bindingScope = null;
        }

        try
        {
            _root.ResumeLayout(true);
        }
        catch (ObjectDisposedException)
        {
            // Ignore shutdown races.
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "LayoutResumeFailed {Context}", context);
        }

        ScheduleResume(context);
    }

    private void CancelPendingResume()
    {
        lock (_debounceGate)
        {
            _pendingResumeContext = null;

            if (_debounceTimerDisposed)
            {
                return;
            }

            if (_resumeDebounceTimer.Enabled)
            {
                _resumeDebounceTimer.Stop();
            }
        }
    }

    private void ScheduleResume(string? context)
    {
        lock (_debounceGate)
        {
            if (_debounceTimerDisposed)
            {
                Volatile.Write(ref _applying, 0);
                return;
            }

            _pendingResumeContext = context;
            _resumeDebounceTimer.Stop();
            _resumeDebounceTimer.Start();
        }

        Volatile.Write(ref _applying, 0);
    }

    private void ResumeDebounceTimerOnTick(object? sender, EventArgs e)
    {
        string? context;

        lock (_debounceGate)
        {
            if (_debounceTimerDisposed)
            {
                return;
            }

            _resumeDebounceTimer.Stop();
            context = _pendingResumeContext;
            _pendingResumeContext = null;
        }

        try
        {
            _resumePreview?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "PreviewResumeFailed {Context}", context);
        }

        _logger.Information("LeaveEditTab {Context}", context);
    }

    private void AttachUiEventHandlers()
    {
        if (_uiEventHandlersAttached)
        {
            return;
        }

        _root.Enter += OnRootEnter;
        _root.Leave += OnRootLeave;
        _root.SizeChanged += OnRootSizeChanged;
        _uiEventHandlersAttached = true;
    }

    private void DetachUiEventHandlers()
    {
        if (!_uiEventHandlersAttached)
        {
            return;
        }

        _root.Enter -= OnRootEnter;
        _root.Leave -= OnRootLeave;
        _root.SizeChanged -= OnRootSizeChanged;
        _uiEventHandlersAttached = false;
    }

    private void DisposeTimers()
    {
        lock (_debounceGate)
        {
            if (!_debounceTimerDisposed)
            {
                _debounceTimerDisposed = true;
                _resumeDebounceTimer.Stop();
                _resumeDebounceTimer.Dispose();
            }
        }

        lock (_uiEventDebounceGate)
        {
            if (!_uiEventDebounceTimerDisposed)
            {
                _uiEventDebounceTimerDisposed = true;
                _uiEventDebounceTimer.Stop();
                _uiEventDebounceTimer.Dispose();
            }
        }

        DetachUiEventHandlers();
    }

    private void OnRootEnter(object? sender, EventArgs e)
    {
        ScheduleApplyAppTypeUi();
    }

    private void OnRootLeave(object? sender, EventArgs e)
    {
        ScheduleApplyAppTypeUi();
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        ScheduleApplyAppTypeUi();
    }

    private void ScheduleApplyAppTypeUi()
    {
        if (_applyAppTypeUiMethod is null)
        {
            return;
        }

        lock (_uiEventDebounceGate)
        {
            if (_uiEventDebounceTimerDisposed)
            {
                return;
            }

            _uiEventDebounceTimer.Stop();
            _uiEventDebounceTimer.Start();
        }
    }

    private void OnUiEventDebounceTimerTick(object? sender, EventArgs e)
    {
        MethodInfo? applyMethod;

        lock (_uiEventDebounceGate)
        {
            if (_uiEventDebounceTimerDisposed)
            {
                return;
            }

            _uiEventDebounceTimer.Stop();
            applyMethod = _applyAppTypeUiMethod;
        }

        if (applyMethod is null)
        {
            return;
        }

        if (_root.IsDisposed || !_root.IsHandleCreated)
        {
            return;
        }

        try
        {
            _root.BeginInvoke(new Action(() =>
            {
                try
                {
                    applyMethod.Invoke(_root, null);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (TargetInvocationException ex)
                {
                    _logger.Warning(ex.InnerException ?? ex, "ApplyAppTypeUIInvokeFailed");
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "ApplyAppTypeUIInvokeFailed");
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public readonly struct EditScope : IDisposable
    {
        private readonly TabEditCoordinator? _owner;
        private readonly string? _context;

        internal EditScope(TabEditCoordinator? owner, string? context, bool isActive)
        {
            _owner = owner;
            _context = context;
            IsActive = isActive;
        }

        public bool IsActive { get; }

        public void Dispose()
        {
            _owner?.EndEditScope(_context);
        }
    }
}
