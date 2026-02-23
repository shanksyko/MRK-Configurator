using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly INetworkAvailabilityService _networkAvailabilityService;
    private readonly IDialogHost _dialogHost;

    private CancellationTokenSource? _executionCts;
    private Task? _executionTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileExecutor"/> class.
    /// </summary>
    /// <param name="windowTimeout">Optional timeout applied when waiting for application windows.</param>
    /// <param name="displayService">Display service used to query monitor metadata.</param>
    public ProfileExecutor(
        TimeSpan? windowTimeout = null,
        IDisplayService? displayService = null,
        INetworkAvailabilityService? networkAvailabilityService = null,
        IDialogHost? dialogHost = null)
    {
        _windowTimeout = windowTimeout ?? DefaultWindowTimeout;

        if (OperatingSystem.IsWindows())
        {
            _displayService = displayService ?? new DisplayService();
            _ownsDisplayService = displayService is null;
        }

        _networkAvailabilityService = networkAvailabilityService ?? new NetworkAvailabilityService();
        _dialogHost = dialogHost ?? NullDialogHost.Instance;
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

        Interlocked.Exchange(ref _executionCts, null)?.Dispose();
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
                Interlocked.Exchange(ref _executionCts, null)?.Dispose();
            }
        }
    }

    private async Task ExecuteProfileAsync(ProfileConfig profile, CancellationToken cancellationToken)
    {
        var monitorSnapshot = CaptureMonitorSnapshot();

        var orderedApps = profile
            .Applications
            .Select((application, index) => (application, index))
            .OrderBy(tuple => tuple.application.Order)
            .ThenBy(tuple => tuple.index)
            .Select(tuple => tuple.application);

        foreach (var app in orderedApps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await ShouldLaunchApplicationAsync(profile, app, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await LaunchAndPositionAppAsync(profile, app, monitorSnapshot, cancellationToken).ConfigureAwait(false);

            if (app.DelayMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(app.DelayMs), cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var window in profile.Windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyStandaloneWindow(profile, window, monitorSnapshot);
        }
    }

    private async Task<bool> ShouldLaunchApplicationAsync(
        ProfileConfig profile,
        AppConfig app,
        CancellationToken cancellationToken)
    {
        if (!app.AutoStart)
        {
            return false;
        }

        if (app.RequiresNetwork)
        {
            var isAvailable = true;

            try
            {
                isAvailable = _networkAvailabilityService.IsNetworkAvailable();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, ex));
                return false;
            }

            if (!isAvailable)
            {
                var exception = new InvalidOperationException($"Rede indisponível para '{app.Id}'.");
                Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, exception));
                return false;
            }
        }

        if (app.AskBeforeLaunch)
        {
            bool shouldLaunch;
            try
            {
                shouldLaunch = await _dialogHost
                    .ShowConfirmationAsync(
                        "Confirmação",
                        $"Deseja iniciar '{app.Id}' agora?",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, ex));
                return false;
            }

            if (!shouldLaunch)
            {
                var exception = new InvalidOperationException($"A execução de '{app.Id}' foi ignorada pelo operador.");
                Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, exception));
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<MonitorInfo> CaptureMonitorSnapshot()
    {
        if (_displayService is null)
        {
            return Array.Empty<MonitorInfo>();
        }

        try
        {
            return _displayService.Monitors().ToList();
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

        Process? process = null;

        try
        {
            if (!File.Exists(app.ExecutablePath))
            {
                throw new FileNotFoundException("Executable not found.", app.ExecutablePath);
            }

            var monitor = ResolveMonitor(profile, app.Window, app.TargetMonitorStableId, monitors);
            var bounds = ResolveBounds(app.Window, monitor);

            // Check if the application is already running (common for browsers that
            // delegate to an existing instance and exit immediately).
            // When arguments are provided (e.g. a URL), always launch the process so
            // that the existing instance receives the new arguments (e.g. opens a new tab).
            var hasArguments = !string.IsNullOrWhiteSpace(app.Arguments);
            var existing = hasArguments ? null : FindRunningProcess(app.ExecutablePath);
            if (existing is not null)
            {
                try
                {
                    AppStarted?.Invoke(this, new AppExecutionEventArgs(profile, app, existing.Id, null));

                    existing.Refresh();
                    var handle = existing.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        handle = await WaitForMainWindowAsync(existing, _windowTimeout, cancellationToken).ConfigureAwait(false);
                    }

                    WindowMover.MoveTo(handle, bounds, app.Window.AlwaysOnTop, restoreIfMinimized: true);
                    AppPositioned?.Invoke(this, new AppExecutionEventArgs(profile, app, existing.Id, monitor));
                }
                finally
                {
                    existing.Dispose();
                }

                return;
            }

            var startInfo = BuildStartInfo(app);
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch process.");

            AppStarted?.Invoke(this, new AppExecutionEventArgs(profile, app, process.Id, null));

            IntPtr windowHandle;
            try
            {
                windowHandle = await WaitForMainWindowAsync(process, _windowTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited immediately — likely a browser that delegated to
                // an existing instance. Find the running instance and position it.
                var fallback = FindRunningProcess(app.ExecutablePath);
                if (fallback is null)
                {
                    throw;
                }

                try
                {
                    fallback.Refresh();
                    windowHandle = fallback.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero)
                    {
                        windowHandle = await WaitForMainWindowAsync(fallback, _windowTimeout, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    fallback.Dispose();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            WindowMover.MoveTo(windowHandle, bounds, app.Window.AlwaysOnTop, restoreIfMinimized: true);
            AppPositioned?.Invoke(this, new AppExecutionEventArgs(profile, app, process.Id, monitor));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, new ProfileExecutionFailedEventArgs(profile, app, null, ex));
        }
        finally
        {
            process?.Dispose();
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
            var bounds = ResolveBounds(window, monitor);
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

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException("The process exited before the main window was ready.");
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            process.Refresh();
            handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        throw new TimeoutException("The main window was not detected within the timeout interval.");
    }

    private static Process? FindRunningProcess(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(executablePath);
            var processName = Path.GetFileNameWithoutExtension(normalized);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            var candidates = Process.GetProcessesByName(processName);
            Process? match = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    var module = candidate.MainModule;
                    var candidatePath = module?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidatePath) &&
                        string.Equals(Path.GetFullPath(candidatePath), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        match = candidate;
                        break;
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Ignore processes without access rights.
                }
                catch (InvalidOperationException)
                {
                    // Ignore processes that exited while enumerating.
                }
            }

            foreach (var candidate in candidates)
            {
                if (!ReferenceEquals(candidate, match))
                {
                    candidate.Dispose();
                }
            }

            return match;
        }
        catch
        {
            return null;
        }
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

        // Use MonitorIdentifier-based matching (same approach as AppTestRunner)
        // to handle topology changes and format variations in stored IDs.
        var normalizedExplicit = NormalizeMonitorId(explicitStableId);
        if (normalizedExplicit is not null)
        {
            var byId = FindMonitorByNormalizedId(monitors, normalizedExplicit);
            if (byId is not null)
            {
                return byId;
            }
        }

        var normalizedDefault = NormalizeMonitorId(profile.DefaultMonitorId);
        if (normalizedDefault is not null && normalizedDefault != normalizedExplicit)
        {
            var byId = FindMonitorByNormalizedId(monitors, normalizedDefault);
            if (byId is not null)
            {
                return byId;
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

    private static MonitorInfo? FindMonitorByNormalizedId(IReadOnlyList<MonitorInfo> monitors, string normalizedId)
    {
        foreach (var monitor in monitors)
        {
            var monitorId = MonitorIdentifier.Normalize(MonitorIdentifier.Create(monitor));
            if (string.Equals(monitorId, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }

            var stableNormalized = MonitorIdentifier.Normalize(monitor.StableId);
            if (!string.IsNullOrEmpty(stableNormalized) &&
                string.Equals(stableNormalized, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    private static string? NormalizeMonitorId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = MonitorIdentifier.Normalize(value);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? NormalizeStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static Rectangle ResolveBounds(WindowConfig window, MonitorInfo monitor)
    {
        var monitorBounds = GetMonitorBounds(monitor);
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            monitorBounds = new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        }

        if (window.FullScreen)
        {
            return monitorBounds;
        }

        var left = window.X.HasValue
            ? monitorBounds.Left + window.X.Value
            : monitorBounds.Left;
        var top = window.Y.HasValue
            ? monitorBounds.Top + window.Y.Value
            : monitorBounds.Top;
        var width = window.Width.HasValue
            ? window.Width.Value
            : monitorBounds.Width;
        var height = window.Height.HasValue
            ? window.Height.Value
            : monitorBounds.Height;

        if (width <= 0)
        {
            width = monitorBounds.Width;
        }

        if (height <= 0)
        {
            height = monitorBounds.Height;
        }

        var target = new Rectangle(left, top, width, height);

        if (TryGetMonitorAreas(monitor, out _, out var workArea))
        {
            target = ClampToWorkArea(target, workArea);
        }

        return target;
    }

    private static Rectangle ClampToWorkArea(Rectangle target, Rectangle workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return target;
        }

        var width = Math.Min(target.Width, workArea.Width);
        var height = Math.Min(target.Height, workArea.Height);

        var maxLeft = Math.Max(workArea.Left, workArea.Right - width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - height);

        var x = Math.Clamp(target.Left, workArea.Left, maxLeft);
        var y = Math.Clamp(target.Top, workArea.Top, maxTop);

        return new Rectangle(x, y, width, height);
    }

    private static Rectangle GetMonitorBounds(MonitorInfo monitor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName) && TryGetDeviceBounds(monitor.DeviceName, out var bounds))
        {
            return bounds;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId) && TryGetDeviceBounds(monitor.Key.DeviceId, out bounds))
        {
            return bounds;
        }

        return new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
    }

    private static bool TryGetMonitorAreas(MonitorInfo monitor, out Rectangle bounds, out Rectangle workArea)
    {
        bounds = Rectangle.Empty;
        workArea = Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var baseBounds = GetMonitorBounds(monitor);
        if (baseBounds.Width <= 0 || baseBounds.Height <= 0)
        {
            return false;
        }

        var point = new POINT
        {
            X = baseBounds.Left + (baseBounds.Width / 2),
            Y = baseBounds.Top + (baseBounds.Height / 2),
        };

        var handle = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var info = MONITORINFOEX.Create();
        if (!GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        workArea = Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        return true;
    }

    private static bool TryGetDeviceBounds(string deviceName, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var devMode = new DEVMODE
        {
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
        };

        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref devMode, 0))
        {
            return false;
        }

        var width = ClampToInt(devMode.dmPelsWidth);
        var height = ClampToInt(devMode.dmPelsHeight);
        bounds = new Rectangle(devMode.dmPositionX, devMode.dmPositionY, width, height);
        return width > 0 && height > 0;
    }

    private static IntPtr FindWindowHandle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return IntPtr.Zero;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        IntPtr result = IntPtr.Zero;

        var builder = new StringBuilder(256);

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

            builder.EnsureCapacity(length + 1);
            builder.Clear();
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

    private static int ClampToInt(uint value)
        => value > int.MaxValue ? int.MaxValue : (int)value;

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProfileExecutor));
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    private const int EnumCurrentSettings = -1;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        private const int CchDevicename = 32;

        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDevicename)]
        public string szDevice;

        public static MONITORINFOEX Create()
        {
            return new MONITORINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
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
