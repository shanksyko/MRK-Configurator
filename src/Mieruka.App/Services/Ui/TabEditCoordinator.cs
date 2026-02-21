using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using Mieruka.Core.Contracts;
using Mieruka.Core.Diagnostics;
using Serilog;

using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Coordinates layout suspension and binding batches when the editor tab is reconfigured.
/// </summary>
internal sealed class TabEditCoordinator
{
    private const int ResumeDebounceIntervalMilliseconds = 150;
    private const int UiEventDebounceIntervalMilliseconds = 120;

    private readonly WinForms.Control _root;
    private readonly IBindingService? _bindingService;
    private readonly Action? _pausePreview;
    private readonly Action? _resumePreview;
    private readonly ILogger _logger;
    private readonly WinForms.Timer _resumeDebounceTimer;
    private readonly object _debounceGate = new();
    private readonly WinForms.Timer _uiEventDebounceTimer;
    private readonly object _uiEventDebounceGate = new();
    private readonly MethodInfo? _applyAppTypeUiMethod;

    private Size _lastRootSize;

    private int _applying;
    private IDisposable? _bindingScope;
    private string? _pendingResumeContext;
    private bool _debounceTimerDisposed;
    private bool _uiEventDebounceTimerDisposed;
    private bool _uiEventHandlersAttached;

    public TabEditCoordinator(
        WinForms.Control root,
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

        _resumeDebounceTimer = new WinForms.Timer
        {
            Interval = ResumeDebounceIntervalMilliseconds,
        };
        _resumeDebounceTimer.Tick += ResumeDebounceTimerOnTick;

        _uiEventDebounceTimer = new WinForms.Timer
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
        using var guard = new StackGuard(nameof(BeginEditScope));
        if (!guard.Entered)
        {
            return new EditScope(null, context, false);
        }

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
        using var guard = new StackGuard(nameof(EndEditScope));
        if (!guard.Entered)
        {
            return;
        }

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
        using var guard = new StackGuard(nameof(ScheduleResume));
        if (!guard.Entered)
        {
            return;
        }

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
        using var guard = new StackGuard(nameof(ResumeDebounceTimerOnTick));
        if (!guard.Entered)
        {
            return;
        }

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
        using var guard = new StackGuard(nameof(OnRootEnter));
        if (!guard.Entered)
        {
            return;
        }

        ScheduleApplyAppTypeUi();
    }

    private void OnRootLeave(object? sender, EventArgs e)
    {
        using var guard = new StackGuard(nameof(OnRootLeave));
        if (!guard.Entered)
        {
            return;
        }

        ScheduleApplyAppTypeUi();
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        using var guard = new StackGuard(nameof(OnRootSizeChanged));
        if (!guard.Entered)
        {
            return;
        }

        var currentSize = _root.Size;
        if (currentSize == _lastRootSize)
        {
            return;
        }

        _lastRootSize = currentSize;

        ScheduleApplyAppTypeUi();
    }

    private void ScheduleApplyAppTypeUi()
    {
        using var guard = new StackGuard(nameof(ScheduleApplyAppTypeUi));
        if (!guard.Entered)
        {
            return;
        }

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
        using var guard = new StackGuard(nameof(OnUiEventDebounceTimerTick));
        if (!guard.Entered)
        {
            return;
        }

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
