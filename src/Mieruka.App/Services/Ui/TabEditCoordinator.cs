using System;
using System.Threading;
using System.Windows.Forms;
using Mieruka.Core.Contracts;
using Serilog;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Coordinates layout suspension and binding batches when the editor tab is reconfigured.
/// </summary>
internal sealed class TabEditCoordinator
{
    private readonly Control _root;
    private readonly IBindingService? _bindingService;
    private readonly Action? _pausePreview;
    private readonly Action? _resumePreview;
    private readonly ILogger _logger;

    private int _applying;
    private IDisposable? _bindingScope;

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
    }

    /// <summary>
    /// Begins a guarded edit scope.
    /// </summary>
    public EditScope BeginEditScope(string? context = null)
    {
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

        try
        {
            _resumePreview?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "PreviewResumeFailed {Context}", context);
        }

        _logger.Information("LeaveEditTab {Context}", context);
        Volatile.Write(ref _applying, 0);
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
