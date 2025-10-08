using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.Automation.Execution;

/// <summary>
/// Executes application profiles by launching native processes and applying their window layouts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProfileExecutor : IDisposable
{
    private static readonly TimeSpan DefaultWindowTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly IDisplayService? _displayService;
    private readonly bool _ownsDisplayService;
    private readonly TimeSpan _windowTimeout;
    private readonly AppRunner _appRunner;

    private CancellationTokenSource? _executionCts;
    private Task? _executionTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileExecutor"/> class.
    /// </summary>
    /// <param name="windowTimeout">Optional timeout applied when waiting for application windows.</param>
    /// <param name="displayService">Display service used to query monitor metadata.</param>
    public ProfileExecutor(TimeSpan? windowTimeout = null, IDisplayService? displayService = null, AppRunner? appRunner = null)
    {
        _windowTimeout = windowTimeout ?? DefaultWindowTimeout;

        if (OperatingSystem.IsWindows())
        {
            _displayService = displayService ?? new DisplayService();
            _ownsDisplayService = displayService is null;
        }

        _appRunner = appRunner ?? new AppRunner(_windowTimeout);
    }

    /// <summary>
    /// Occurs when an application process has been started.
    /// </summary>
    public event EventHandler<AppExecutionEventArgs>? AppStarted;

    /// <summary>
    /// Occurs after a window has been positioned on the target monitor.
    /// </summary>
    public event EventHandler<AppExecutionEventArgs>? AppPositioned;

    /// <summary>
    /// Occurs after the profile execution finishes.
    /// </summary>
    public event EventHandler<ProfileExecutionCompletedEventArgs>? Completed;

    /// <summary>
    /// Occurs when an application or window fails during execution.
    /// </summary>
    public event EventHandler<ProfileExecutionFailedEventArgs>? Failed;

    /// <summary>
    /// Starts executing the supplied profile.
    /// </summary>
    /// <param name="profile">Profile that should be executed.</param>
    /// <param name="cancellationToken">Token used to cancel the execution.</param>
    /// <returns>A task that completes when the profile execution finishes.</returns>
    public Task Start(ProfileConfig profile, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(profile);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Profile execution is only supported on Windows.");
        }

        lock (_gate)
        {
            if (_executionTask is not null && !_executionTask.IsCompleted)
            {
                throw new InvalidOperationException("Another profile is already running.");
            }

            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _executionCts.Token;
            _executionTask = RunProfileAsync(profile, token);
            return _executionTask;
        }
    }

    /// <summary>
    /// Requests the cancellation of the current profile execution.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _executionCts?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();

        if (_ownsDisplayService)
        {
            _displayService?.Dispose();
        }

        _executionCts?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task RunProfileAsync(ProfileConfig profile, CancellationToken cancellationToken)
    {
        var cancelled = false;

        try
        {
            await ExecuteProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, null, null, ex));
        }
        finally
        {
            Completed?.Invoke(this, new ProfileExecutionCompletedEventArgs(profile, cancelled));

            lock (_gate)
            {
                _executionTask = null;
                _executionCts?.Dispose();
                _executionCts = null;
            }
        }
    }

    private async Task ExecuteProfileAsync(ProfileConfig profile, CancellationToken cancellationToken)
    {
        var monitorSnapshot = CaptureMonitorSnapshot();

        foreach (var app in profile.Applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LaunchAndPositionAppAsync(profile, app, monitorSnapshot, cancellationToken).ConfigureAwait(false);
        }

        foreach (var window in profile.Windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyStandaloneWindow(profile, window, monitorSnapshot);
        }
    }

    private IReadOnlyList<MonitorInfo> CaptureMonitorSnapshot()
    {
        if (_displayService is null)
        {
            return Array.Empty<MonitorInfo>();
        }

        try
        {
            var monitors = _displayService.Monitors();
            if (monitors is List<MonitorInfo> list)
            {
                return list.ToList();
            }

            return monitors.ToList();
        }
        catch
        {
            return Array.Empty<MonitorInfo>();
        }
    }

    private async Task LaunchAndPositionAppAsync(
        ProfileConfig profile,
        AppConfig app,
        IReadOnlyList<MonitorInfo> monitors,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return;
        }

        int? processId = null;

        try
        {
            if (!File.Exists(app.ExecutablePath))
            {
                throw new FileNotFoundException("Executable not found.", app.ExecutablePath);
            }

            var startInfo = BuildStartInfo(app);
            var monitor = ResolveMonitor(profile, app.Window, app.TargetMonitorStableId, monitors);

            var positioned = await _appRunner.RunAndPositionAsync(
                startInfo,
                monitor,
                app.Window,
                cancellationToken,
                process =>
                {
                    processId = process.Id;
                    AppStarted?.Invoke(this, new AppExecutionEventArgs(profile, app, process.Id, null));
                }).ConfigureAwait(false);

            if (positioned)
            {
                AppPositioned?.Invoke(this, new AppExecutionEventArgs(profile, app, processId, monitor));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, ex));
        }
    }

    private void ApplyStandaloneWindow(ProfileConfig profile, WindowConfig window, IReadOnlyList<MonitorInfo> monitors)
    {
        if (string.IsNullOrWhiteSpace(window.Title))
        {
            return;
        }

        try
        {
            var handle = FindWindowHandle(window.Title);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var monitor = ResolveMonitor(profile, window, profile.DefaultMonitorId, monitors);
            var bounds = AppRunner.ResolveBounds(window, monitor);
            WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: false);

            AppPositioned?.Invoke(this, new AppExecutionEventArgs(profile, null, null, monitor, window.Title));
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, null, window, ex));
        }
    }

    private static ProcessStartInfo BuildStartInfo(AppConfig app)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = app.ExecutablePath,
            Arguments = string.IsNullOrWhiteSpace(app.Arguments) ? string.Empty : app.Arguments,
            UseShellExecute = false,
        };

        try
        {
            var directory = Path.GetDirectoryName(app.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                startInfo.WorkingDirectory = directory;
            }
        }
        catch
        {
            // Ignore invalid working directories and fallback to the process default.
        }

        foreach (var pair in app.EnvironmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        return startInfo;
    }

    private MonitorInfo ResolveMonitor(
        ProfileConfig profile,
        WindowConfig window,
        string? explicitStableId,
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (_displayService is not null)
        {
            var liveMonitor = _displayService.FindBy(window.Monitor);
            if (liveMonitor is not null)
            {
                return liveMonitor;
            }
        }

        foreach (var monitor in monitors)
        {
            if (MonitorKeysEqual(monitor.Key, window.Monitor))
            {
                return monitor;
            }
        }

        var stableId = NormalizeStableId(explicitStableId) ?? NormalizeStableId(profile.DefaultMonitorId);
        if (stableId is not null)
        {
            var byStable = FindMonitorByStableId(monitors, stableId);
            if (byStable is not null)
            {
                return byStable;
            }
        }

        if (monitors.Count > 0)
        {
            return monitors[0];
        }

        return new MonitorInfo();
    }

    private static MonitorInfo? FindMonitorByStableId(IReadOnlyList<MonitorInfo> monitors, string stableId)
    {
        foreach (var monitor in monitors)
        {
            if (string.Equals(NormalizeStableId(monitor.StableId), stableId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeStableId(monitor.DeviceName), stableId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeStableId(monitor.Key.DeviceId), stableId, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    private static string? NormalizeStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static IntPtr FindWindowHandle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return IntPtr.Zero;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        IntPtr result = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);
            if (GetWindowText(hwnd, builder, builder.Capacity) == 0)
            {
                return true;
            }

            var current = builder.ToString();
            if (string.Equals(current, title, comparison))
            {
                result = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey right)
    {
        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProfileExecutor));
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

}

/// <summary>
/// Describes an event related to an application execution.
/// </summary>
public sealed class AppExecutionEventArgs : EventArgs
{
    internal AppExecutionEventArgs(ProfileConfig profile, AppConfig? application, int? processId, MonitorInfo? monitor, string? windowTitle = null)
    {
        Profile = profile;
        Application = application;
        ProcessId = processId;
        Monitor = monitor;
        WindowTitle = windowTitle ?? application?.Window?.Title ?? string.Empty;
    }

    /// <summary>
    /// Gets the profile being executed.
    /// </summary>
    public ProfileConfig Profile { get; }

    /// <summary>
    /// Gets the application related to the event when available.
    /// </summary>
    public AppConfig? Application { get; }

    /// <summary>
    /// Gets the process identifier associated with the event.
    /// </summary>
    public int? ProcessId { get; }

    /// <summary>
    /// Gets the monitor where the window was positioned.
    /// </summary>
    public MonitorInfo? Monitor { get; }

    /// <summary>
    /// Gets the title associated with the window that triggered the event.
    /// </summary>
    public string WindowTitle { get; }

    /// <summary>
    /// Gets a friendly name displayed in the user interface.
    /// </summary>
    public string DisplayName => Application?.Id ?? WindowTitle;
}

/// <summary>
/// Provides information about the completion of a profile execution.
/// </summary>
public sealed class ProfileExecutionCompletedEventArgs : EventArgs
{
    internal ProfileExecutionCompletedEventArgs(ProfileConfig profile, bool cancelled)
    {
        Profile = profile;
        Cancelled = cancelled;
    }

    /// <summary>
    /// Gets the profile that was executed.
    /// </summary>
    public ProfileConfig Profile { get; }

    /// <summary>
    /// Gets a value indicating whether the execution was cancelled.
    /// </summary>
    public bool Cancelled { get; }
}

/// <summary>
/// Represents an execution failure for a profile entry.
/// </summary>
public sealed class ProfileExecutionFailedEventArgs : EventArgs
{
    internal ProfileExecutionFailedEventArgs(ProfileConfig profile, AppConfig? application, WindowConfig? window, Exception exception)
    {
        Profile = profile;
        Application = application;
        Window = window;
        Exception = exception;
    }

    /// <summary>
    /// Gets the profile associated with the failure.
    /// </summary>
    public ProfileConfig Profile { get; }

    /// <summary>
    /// Gets the application that failed, when available.
    /// </summary>
    public AppConfig? Application { get; }

    /// <summary>
    /// Gets the window configuration associated with the failure, when available.
    /// </summary>
    public WindowConfig? Window { get; }

    /// <summary>
    /// Gets the exception that describes the failure.
    /// </summary>
    public Exception Exception { get; }
}
