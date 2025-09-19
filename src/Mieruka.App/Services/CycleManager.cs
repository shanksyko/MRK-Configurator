using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.App.Services;

/// <summary>
/// Controls the playback of configured entries, rotating their windows on demand.
/// </summary>
public sealed class CycleManager : IOrchestrationComponent, IDisposable
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly BindingService _bindingService;
    private readonly ITelemetry _telemetry;
    private readonly object _gate = new();
    private readonly Dictionary<string, AppConfig> _applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SiteConfig> _sites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private readonly HotkeyManager? _hotkeyManager;
    private readonly bool _ownsHotkeyManager;

    private const string PlayPauseHotkeyKey = "Cycle.PlayPause";
    private const string NextHotkeyKey = "Cycle.Next";
    private const string PreviousHotkeyKey = "Cycle.Previous";
    private const string ReapplyBindingsHotkeyKey = "Cycle.ReapplyBindings";

    private List<CycleTargetContext> _playlist = new();
    private CycleConfig _cycleConfig = new();
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private Timer? _timer;
    private DateTimeOffset _currentStartTime = DateTimeOffset.MinValue;
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private TimeSpan _remainingWhenPaused = TimeSpan.Zero;
    private int _currentIndex;
    private bool _disposed;
    private bool _hasConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CycleManager"/> class.
    /// </summary>
    /// <param name="bindingService">Service responsible for applying window placements.</param>
    /// <param name="hotkeyManager">Manager used to register global hotkeys.</param>
    /// <param name="telemetry">Optional telemetry sink used to record playback events.</param>
    public CycleManager(BindingService bindingService, HotkeyManager? hotkeyManager = null, ITelemetry? telemetry = null)
    {
        _bindingService = bindingService ?? throw new ArgumentNullException(nameof(bindingService));
        _telemetry = telemetry ?? NullTelemetry.Instance;

        if (hotkeyManager is not null)
        {
            _hotkeyManager = hotkeyManager;
            _ownsHotkeyManager = false;
        }
        else if (OperatingSystem.IsWindows())
        {
            _hotkeyManager = new HotkeyManager(_telemetry);
            _ownsHotkeyManager = true;
        }
    }

    /// <summary>
    /// Applies the supplied configuration to the cycle manager.
    /// </summary>
    /// <param name="config">Configuration containing the cycle definition.</param>
    public void ApplyConfiguration(GeneralConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        bool shouldStop = false;

        lock (_gate)
        {
            EnsureNotDisposed();

            _applications.Clear();
            foreach (var app in config.Applications)
            {
                if (!string.IsNullOrWhiteSpace(app.Id))
                {
                    _applications[app.Id] = app;
                }
            }

            _sites.Clear();
            foreach (var site in config.Sites)
            {
                if (!string.IsNullOrWhiteSpace(site.Id))
                {
                    _sites[site.Id] = site;
                }
            }

            _cycleConfig = config.Cycle ?? new CycleConfig();
            _hasConfiguration = true;

            RebuildPlaylistLocked();

            if ((_playlist.Count == 0 || !_cycleConfig.Enabled) && _playbackState == PlaybackState.Playing)
            {
                _playbackState = PlaybackState.Stopped;
                _remainingWhenPaused = TimeSpan.Zero;
                shouldStop = true;
            }
        }

        if (shouldStop)
        {
            StopTimer();
        }

        RefreshHotkeys();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureNotDisposed();

            if (!_hasConfiguration)
            {
                throw new InvalidOperationException("Configuration must be applied before starting playback.");
            }

            if (_playbackState == PlaybackState.Playing)
            {
                return Task.CompletedTask;
            }

            if (!_cycleConfig.Enabled || _playlist.Count == 0)
            {
                _telemetry.Info("Cycle playback is disabled or no items are available.");
                _playbackState = PlaybackState.Stopped;
                return Task.CompletedTask;
            }

            _playbackState = PlaybackState.Playing;
        }

        if (!TryActivateWithFallback(_currentIndex, 1, scheduleRetryOnFailure: true))
        {
            _telemetry.Warn("Unable to locate any window for playback. Retrying automatically.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopInternal();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        StopInternal();
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes playback, starting it when it is currently stopped.
    /// </summary>
    public void Play()
    {
        EnsureNotDisposed();
        ResumeInternal();
    }

    /// <summary>
    /// Pauses playback when it is running.
    /// </summary>
    public void Pause()
    {
        EnsureNotDisposed();
        PauseInternal();
    }

    /// <summary>
    /// Advances to the next item in the playback sequence.
    /// </summary>
    public void Next()
    {
        EnsureNotDisposed();
        Navigate(1);
    }

    /// <summary>
    /// Returns to the previous item in the playback sequence.
    /// </summary>
    public void Previous()
    {
        EnsureNotDisposed();
        Navigate(-1);
    }

    /// <summary>
    /// Releases resources used by the cycle manager.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopInternal();
        ClearHotkeys();

        _timer?.Dispose();

        if (_ownsHotkeyManager)
        {
            _hotkeyManager?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void StopInternal()
    {
        lock (_gate)
        {
            if (_playbackState == PlaybackState.Stopped)
            {
                return;
            }

            _playbackState = PlaybackState.Stopped;
            _remainingWhenPaused = TimeSpan.Zero;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void PauseInternal()
    {
        DateTimeOffset start;
        TimeSpan duration;

        lock (_gate)
        {
            if (_playbackState != PlaybackState.Playing)
            {
                return;
            }

            _playbackState = PlaybackState.Paused;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            start = _currentStartTime;
            duration = _currentDuration;
        }

        var elapsed = DateTimeOffset.UtcNow - start;
        var remaining = duration - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        lock (_gate)
        {
            _remainingWhenPaused = remaining;
        }

        _telemetry.Info("Cycle playback paused.");
    }

    private void ResumeInternal()
    {
        CycleTargetContext? context = null;
        TimeSpan dueTime = TimeSpan.Zero;
        bool shouldStart = false;

        lock (_gate)
        {
            if (!_hasConfiguration)
            {
                throw new InvalidOperationException("Configuration must be applied before resuming playback.");
            }

            if (!_cycleConfig.Enabled || _playlist.Count == 0)
            {
                _telemetry.Info("Cycle playback cannot resume because there are no enabled items.");
                return;
            }

            switch (_playbackState)
            {
                case PlaybackState.Playing:
                    return;

                case PlaybackState.Paused:
                    context = _playlist[_currentIndex];
                    dueTime = _remainingWhenPaused > TimeSpan.Zero ? _remainingWhenPaused : _currentDuration;
                    _playbackState = PlaybackState.Playing;
                    _remainingWhenPaused = TimeSpan.Zero;
                    break;

                case PlaybackState.Stopped:
                    _playbackState = PlaybackState.Playing;
                    shouldStart = true;
                    break;
            }
        }

        if (shouldStart)
        {
            if (!TryActivateWithFallback(_currentIndex, 1, scheduleRetryOnFailure: true))
            {
                _telemetry.Warn("Unable to resume playback because no windows are available.");
            }
            else
            {
                _telemetry.Info("Cycle playback started.");
            }

            return;
        }

        if (context is null)
        {
            return;
        }

        if (_bindingService.TryReapplyBinding(context.Kind, context.TargetId, out var handle) && handle != IntPtr.Zero)
        {
            WindowActivator.BringToFront(handle);
        }

        var now = DateTimeOffset.UtcNow;
        ScheduleTimer(dueTime, now);
        _telemetry.Info("Cycle playback resumed.");
    }

    private void Navigate(int step)
    {
        if (step == 0)
        {
            return;
        }

        PlaybackState state;
        lock (_gate)
        {
            state = _playbackState;

            if (state == PlaybackState.Stopped && _playlist.Count == 0)
            {
                return;
            }

            if (state == PlaybackState.Paused)
            {
                _playbackState = PlaybackState.Playing;
            }
        }

        StopTimer();

        var direction = step > 0 ? 1 : -1;
        var scheduleRetry = state != PlaybackState.Paused;

        if (!TryActivateWithFallback(_currentIndex + step, direction, scheduleRetry))
        {
            if (state == PlaybackState.Paused)
            {
                lock (_gate)
                {
                    _playbackState = PlaybackState.Paused;
                }
            }

            return;
        }

        if (state == PlaybackState.Paused)
        {
            PauseInternal();
        }
    }

    private bool TryActivateWithFallback(int startIndex, int step, bool scheduleRetryOnFailure)
    {
        List<CycleTargetContext> snapshot;

        lock (_gate)
        {
            if (!_cycleConfig.Enabled || _playlist.Count == 0)
            {
                return false;
            }

            snapshot = _playlist;
        }

        var count = snapshot.Count;
        if (count == 0)
        {
            return false;
        }

        var direction = step >= 0 ? 1 : -1;
        var index = NormalizeIndex(startIndex, count);

        for (var attempt = 0; attempt < count; attempt++)
        {
            var candidateIndex = NormalizeIndex(index + attempt * direction, count);
            var candidate = snapshot[candidateIndex];

            if (_bindingService.TryReapplyBinding(candidate.Kind, candidate.TargetId, out var handle) && handle != IntPtr.Zero)
            {
                WindowActivator.BringToFront(handle);

                var now = DateTimeOffset.UtcNow;

                lock (_gate)
                {
                    _currentIndex = candidateIndex;
                    _currentDuration = candidate.Duration;
                    _currentStartTime = now;
                    _remainingWhenPaused = TimeSpan.Zero;
                }

                ScheduleTimer(candidate.Duration, now);
                _telemetry.Info($"cycle play {candidate.KindName}:{candidate.TargetId} for {candidate.Duration.TotalSeconds:0.#} s");
                return true;
            }

            _telemetry.Warn($"cycle unavailable {candidate.KindName}:{candidate.TargetId}");
        }

        if (scheduleRetryOnFailure)
        {
            _telemetry.Warn("cycle pending windows; retry scheduled");
            ScheduleRetry();
        }

        return false;
    }

    private void RebuildPlaylistLocked()
    {
        var items = _cycleConfig.Items ?? Array.Empty<CycleItem>();
        var defaultDuration = _cycleConfig.DefaultDurationSeconds > 0 ? _cycleConfig.DefaultDurationSeconds : 60;
        var contexts = new List<CycleTargetContext>(items.Count);

        foreach (var item in items)
        {
            if (item is null || !item.Enabled)
            {
                continue;
            }

            if (!TryCreateContext(item, defaultDuration, out var context))
            {
                continue;
            }

            contexts.Add(context);
        }

        if (_cycleConfig.Shuffle && contexts.Count > 1)
        {
            Shuffle(contexts);
        }

        _playlist = contexts;

        if (_currentIndex >= _playlist.Count)
        {
            _currentIndex = 0;
        }
    }

    private bool TryCreateContext(CycleItem item, int defaultDurationSeconds, out CycleTargetContext context)
    {
        context = null!;

        if (!TryParseKind(item.TargetType, out var kind))
        {
            _telemetry.Warn($"cycle skip '{item.Id}' due to unknown target type '{item.TargetType}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.TargetId))
        {
            _telemetry.Warn($"cycle skip '{item.Id}' because the target identifier is empty.");
            return false;
        }

        switch (kind)
        {
            case EntryKind.Application when !_applications.ContainsKey(item.TargetId):
                _telemetry.Warn($"cycle skip '{item.Id}' because application '{item.TargetId}' was not found.");
                return false;

            case EntryKind.Site when !_sites.ContainsKey(item.TargetId):
                _telemetry.Warn($"cycle skip '{item.Id}' because site '{item.TargetId}' was not found.");
                return false;
        }

        var seconds = item.DurationSeconds > 0 ? item.DurationSeconds : defaultDurationSeconds;
        if (seconds <= 0)
        {
            seconds = 60;
        }

        var duration = TimeSpan.FromSeconds(seconds);
        context = new CycleTargetContext(item.Id, kind, item.TargetId, duration);
        return true;
    }

    private void Shuffle(List<CycleTargetContext> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = _random.Next(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }

    private void ScheduleTimer(TimeSpan dueTime, DateTimeOffset start)
    {
        if (dueTime <= TimeSpan.Zero)
        {
            dueTime = MinimumInterval;
        }

        lock (_gate)
        {
            _timer ??= new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _currentStartTime = start;
            _currentDuration = dueTime;
            _remainingWhenPaused = TimeSpan.Zero;
            _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private void ScheduleRetry()
        => ScheduleTimer(RetryDelay, DateTimeOffset.UtcNow);

    private void StopTimer()
    {
        lock (_gate)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void RefreshHotkeys()
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        CycleHotkeyConfig hotkeys;

        lock (_gate)
        {
            hotkeys = _cycleConfig.Hotkeys ?? new CycleHotkeyConfig();
        }

        RegisterHotkey(PlayPauseHotkeyKey, "Cycle - Play/Pause", hotkeys.PlayPause, TogglePlayback);
        RegisterHotkey(NextHotkeyKey, "Cycle - Next", hotkeys.Next, () => Navigate(1));
        RegisterHotkey(PreviousHotkeyKey, "Cycle - Previous", hotkeys.Previous, () => Navigate(-1));
        RegisterHotkey(ReapplyBindingsHotkeyKey, "Reapply Bindings", hotkeys.ReapplyBindings, () => _bindingService.ReapplyAllBindings());
    }

    private void RegisterHotkey(string key, string displayName, string? gesture, Action handler)
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        _hotkeyManager.RegisterOrUpdate(key, displayName, gesture, handler);
    }

    private void ClearHotkeys()
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        _hotkeyManager.Unregister(PlayPauseHotkeyKey);
        _hotkeyManager.Unregister(NextHotkeyKey);
        _hotkeyManager.Unregister(PreviousHotkeyKey);
        _hotkeyManager.Unregister(ReapplyBindingsHotkeyKey);
    }

    private void TogglePlayback()
    {
        PlaybackState state;
        lock (_gate)
        {
            state = _playbackState;
        }

        switch (state)
        {
            case PlaybackState.Playing:
                PauseInternal();
                break;

            case PlaybackState.Paused:
            case PlaybackState.Stopped:
                ResumeInternal();
                break;
        }
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            PlaybackState snapshot;
            lock (_gate)
            {
                snapshot = _playbackState;
            }

            if (snapshot != PlaybackState.Playing)
            {
                return;
            }

            _ = TryActivateWithFallback(_currentIndex + 1, 1, scheduleRetryOnFailure: true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Unexpected error during cycle playback.", ex);
        }
    }

    private static bool TryParseKind(string? value, out EntryKind kind)
    {
        kind = EntryKind.Application;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("Application", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("App", StringComparison.OrdinalIgnoreCase))
        {
            kind = EntryKind.Application;
            return true;
        }

        if (value.Equals("Site", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Web", StringComparison.OrdinalIgnoreCase))
        {
            kind = EntryKind.Site;
            return true;
        }

        return Enum.TryParse(value, true, out kind);
    }

    private static int NormalizeIndex(int index, int count)
    {
        var result = index % count;
        if (result < 0)
        {
            result += count;
        }

        return result;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CycleManager));
        }
    }

    private enum PlaybackState
    {
        Stopped,
        Playing,
        Paused,
    }

    private sealed record class CycleTargetContext(string ItemId, EntryKind Kind, string TargetId, TimeSpan Duration)
    {
        public string KindName => Kind == EntryKind.Application ? "app" : "site";
    }

    private static class WindowActivator
    {
        private const int SwShow = 5;
        private const int SwRestore = 9;

        public static void BringToFront(IntPtr handle)
        {
            if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            {
                return;
            }

            if (NativeMethods.IsIconic(handle))
            {
                NativeMethods.ShowWindow(handle, SwRestore);
            }
            else
            {
                NativeMethods.ShowWindow(handle, SwShow);
            }

            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == handle)
            {
                return;
            }

            var currentThread = NativeMethods.GetCurrentThreadId();
            var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);

            if (currentThread != targetThread)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, true);
                NativeMethods.SetForegroundWindow(handle);
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(handle);
            }

            NativeMethods.BringWindowToTop(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    }

    private sealed class NullTelemetry : ITelemetry
    {
        public static ITelemetry Instance { get; } = new NullTelemetry();

        public void Error(string message, Exception? exception = null)
        {
        }

        public void Info(string message, Exception? exception = null)
        {
        }

        public void Warn(string message, Exception? exception = null)
        {
        }
    }
}
