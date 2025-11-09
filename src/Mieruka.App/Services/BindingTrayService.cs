using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using Serilog;
using Drawing = System.Drawing;

namespace Mieruka.App.Services;

/// <summary>
/// Associates configuration entries with running windows and ensures they stay in the desired position.
/// </summary>
internal sealed class BindingTrayService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<BindingTrayService>();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(1200);

    private readonly IDisplayService _displayService;
    private readonly ITelemetry _telemetry;
    private readonly object _gate = new();
    private readonly Dictionary<string, WindowBinding<AppConfig>> _appBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WindowBinding<SiteConfig>> _siteBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ZonePreset> _zonePresets = new();

    private CancellationTokenSource? _reapplyCancellation;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingTrayService"/> class.
    /// </summary>
    /// <param name="displayService">Service that provides monitor information.</param>
    /// <param name="telemetry">Telemetry sink used to log reposition operations.</param>
    public BindingTrayService(IDisplayService displayService, ITelemetry? telemetry = null)
    {
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _telemetry = telemetry ?? NullTelemetry.Instance;

        _displayService.TopologyChanged += OnTopologyChanged;
    }

    /// <summary>
    /// Associates an application configuration with a window handle.
    /// </summary>
    /// <param name="config">Configuration entry.</param>
    /// <param name="windowHandle">Handle of the window that should follow the configuration.</param>
    public void Bind(AppConfig config, IntPtr windowHandle)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureNotDisposed();

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowPlacement placement;
        bool shouldTeleport;

        lock (_gate)
        {
            if (!_appBindings.TryGetValue(config.Id, out var binding))
            {
                binding = new WindowBinding<AppConfig>(config, windowHandle, CreatePlacement);
                _appBindings[config.Id] = binding;
                placement = binding.Placement;
                shouldTeleport = true;
                binding.ResetChangeFlags();
            }
            else
            {
                binding.Update(config, windowHandle);
                placement = binding.Placement;
                shouldTeleport = binding.HandleChanged || binding.PlacementChanged;
                binding.ResetChangeFlags();
            }
        }

        if (shouldTeleport)
        {
            ApplyWindow(config.Id, "app", windowHandle, placement);
        }
    }

    /// <summary>
    /// Associates a site configuration with a window handle.
    /// </summary>
    /// <param name="config">Configuration entry.</param>
    /// <param name="windowHandle">Handle of the window that should follow the configuration.</param>
    public void Bind(SiteConfig config, IntPtr windowHandle)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureNotDisposed();

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowPlacement placement;
        bool shouldTeleport;

        lock (_gate)
        {
            if (!_siteBindings.TryGetValue(config.Id, out var binding))
            {
                binding = new WindowBinding<SiteConfig>(config, windowHandle, CreatePlacement);
                _siteBindings[config.Id] = binding;
                placement = binding.Placement;
                shouldTeleport = true;
                binding.ResetChangeFlags();
            }
            else
            {
                binding.Update(config, windowHandle);
                placement = binding.Placement;
                shouldTeleport = binding.HandleChanged || binding.PlacementChanged;
                binding.ResetChangeFlags();
            }
        }

        if (shouldTeleport)
        {
            ApplyWindow(config.Id, "site", windowHandle, placement);
        }
    }

    /// <summary>
    /// Applies configuration metadata that influences window placement.
    /// </summary>
    /// <param name="config">Configuration snapshot containing zone presets.</param>
    public void ApplyConfiguration(GeneralConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            _zonePresets.Clear();
            if (config.ZonePresets is { Count: > 0 })
            {
                _zonePresets.AddRange(config.ZonePresets);
            }
        }
    }

    /// <summary>
    /// Attempts to reapply the window configuration for the specified entry.
    /// </summary>
    /// <param name="kind">Entry category.</param>
    /// <param name="id">Unique identifier of the entry.</param>
    /// <param name="handle">Handle currently associated with the entry.</param>
    /// <returns><c>true</c> when the window was repositioned; otherwise, <c>false</c>.</returns>
    public bool TryReapplyBinding(EntryKind kind, string id, out IntPtr handle)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Entry identifier cannot be empty.", nameof(id));
        }

        handle = IntPtr.Zero;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        WindowPlacement? placement = null;
        string? kindName = null;

        lock (_gate)
        {
            switch (kind)
            {
                case EntryKind.Application when _appBindings.TryGetValue(id, out var appBinding):
                    handle = appBinding.WindowHandle;
                    placement = appBinding.Placement;
                    kindName = "app";
                    break;

                case EntryKind.Site when _siteBindings.TryGetValue(id, out var siteBinding):
                    handle = siteBinding.WindowHandle;
                    placement = siteBinding.Placement;
                    kindName = "site";
                    break;
            }
        }

        if (handle == IntPtr.Zero || placement is null || string.IsNullOrWhiteSpace(kindName))
        {
            handle = IntPtr.Zero;
            return false;
        }

        ApplyWindow(id, kindName, handle, placement.Value);
        return true;
    }

    /// <summary>
    /// Reapplies the window configuration for all tracked bindings.
    /// </summary>
    public void ReapplyAllBindings()
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ReapplyAll();
    }

    /// <summary>
    /// Releases resources associated with the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _displayService.TopologyChanged -= OnTopologyChanged;

        lock (_gate)
        {
            _reapplyCancellation?.Cancel();
            _reapplyCancellation?.Dispose();
            _reapplyCancellation = null;

            _appBindings.Clear();
            _siteBindings.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void OnTopologyChanged(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CancellationTokenSource? pending;

        lock (_gate)
        {
            _reapplyCancellation?.Cancel();
            _reapplyCancellation?.Dispose();

            if (_disposed)
            {
                return;
            }

            pending = new CancellationTokenSource();
            _reapplyCancellation = pending;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, pending.Token).ConfigureAwait(false);

                if (!pending.Token.IsCancellationRequested)
                {
                    ReapplyAll();
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when a new topology change event arrives before the delay elapses.
            }
        }, CancellationToken.None);
    }

    private void ReapplyAll()
    {
        List<WindowReapplyInfo> snapshot;

        lock (_gate)
        {
            snapshot = new List<WindowReapplyInfo>(_appBindings.Count + _siteBindings.Count);

            snapshot.AddRange(_appBindings.Select(pair =>
                new WindowReapplyInfo(pair.Key, "app", pair.Value.WindowHandle, pair.Value.Placement)));

            snapshot.AddRange(_siteBindings.Select(pair =>
                new WindowReapplyInfo(pair.Key, "site", pair.Value.WindowHandle, pair.Value.Placement)));
        }

        foreach (var item in snapshot)
        {
            ApplyWindow(item.Id, item.Kind, item.Handle, item.Placement);
        }
    }

    private void ApplyWindow(string id, string kind, IntPtr handle, WindowPlacement placement)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var monitors = _displayService.Monitors();
            var monitor = ResolveMonitor(monitors, placement);
            if (monitor is null)
            {
                _telemetry.Warn($"Unable to find monitor for {kind} '{id}'.");
                ForEvent("MonitorFallback")
                    .Warning(
                        "Nenhum monitor disponível para {Kind}:{Id}. Solicitado estável '{StableId}' com chave {@MonitorKey}.",
                        kind,
                        id,
                        placement.MonitorStableId,
                        placement.Window.Monitor);
                return;
            }

            var zoneRect = ResolveZoneRect(monitor, placement);
            var targetBounds = WindowPlacementHelper.CalculateZoneBounds(monitor, zoneRect);
            var topMost = placement.Window.AlwaysOnTop || placement.Window.FullScreen;

            if (WindowPlacementHelper.PlaceWindow(handle, monitor, zoneRect, topMost))
            {
                _telemetry.Info($"teleport {kind}:{id} {targetBounds.Left},{targetBounds.Top} {targetBounds.Width}x{targetBounds.Height}");
            }
            else
            {
                _telemetry.Warn($"Failed to reposition window for {kind} '{id}'.");
                Logger.Warning(
                    "Falha ao reposicionar {Kind}:{Id} no monitor {MonitorId}.",
                    kind,
                    id,
                    WindowPlacementHelper.ResolveStableId(monitor));
            }
        }
        catch (Exception ex)
        {
            _telemetry.Error($"Failed to reposition window for {kind} '{id}'.", ex);
            Logger.Error(ex, "Erro ao reposicionar {Kind}:{Id}.", kind, id);
        }
    }

    private MonitorInfo? ResolveMonitor(IReadOnlyList<MonitorInfo> monitors, WindowPlacement placement)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(placement.MonitorStableId))
        {
            var stable = WindowPlacementHelper.GetMonitorByStableId(monitors, placement.MonitorStableId);
            if (stable is not null)
            {
                return stable;
            }

            ForEvent("MonitorFallback")
                .Warning(
                    "Monitor estável '{StableId}' não encontrado; utilizando chave {@MonitorKey}.",
                    placement.MonitorStableId,
                    placement.Window.Monitor);
        }

        var monitor = _displayService.FindBy(placement.Window.Monitor);
        if (monitor is not null)
        {
            return monitor;
        }

        var fallback = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        ForEvent("MonitorFallback")
            .Warning(
                "Nenhum monitor ativo correspondeu à chave {@MonitorKey}; usando fallback {FallbackStableId}.",
                placement.Window.Monitor,
                WindowPlacementHelper.ResolveStableId(fallback));
        return fallback;
    }

    private WindowPlacementHelper.ZoneRect ResolveZoneRect(MonitorInfo monitor, WindowPlacement placement)
    {
        var zoneIdentifier = placement.ZonePresetId;
        var presets = GetZonePresetsSnapshot();

        if (!string.IsNullOrWhiteSpace(zoneIdentifier) &&
            WindowPlacementHelper.TryGetZoneRect(presets, zoneIdentifier, out var zone))
        {
            return zone;
        }

        return WindowPlacementHelper.CreateZoneFromWindow(placement.Window, monitor);
    }

    private IReadOnlyList<ZonePreset> GetZonePresetsSnapshot()
    {
        lock (_gate)
        {
            if (_zonePresets.Count == 0)
            {
                return Array.Empty<ZonePreset>();
            }

            return _zonePresets.ToList();
        }
    }

    private static WindowPlacement CreatePlacement(AppConfig config)
        => new(config.Window with { }, NormalizeStableId(config.TargetMonitorStableId), NormalizeZoneId(config.TargetZonePresetId));

    private static WindowPlacement CreatePlacement(SiteConfig config)
        => new(config.Window with { }, NormalizeStableId(config.TargetMonitorStableId), NormalizeZoneId(config.TargetZonePresetId));

    private static string NormalizeStableId(string? stableId)
        => string.IsNullOrWhiteSpace(stableId) ? string.Empty : stableId.Trim();

    private static string? NormalizeZoneId(string? zoneId)
        => string.IsNullOrWhiteSpace(zoneId) ? null : zoneId.Trim();

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BindingTrayService));
        }
    }

    private sealed class WindowBinding<T>
    {
        private readonly Func<T, WindowPlacement> _placementFactory;
        private WindowPlacement _placement;

        public WindowBinding(T config, IntPtr handle, Func<T, WindowPlacement> placementFactory)
        {
            Config = config;
            WindowHandle = handle;
            _placementFactory = placementFactory ?? throw new ArgumentNullException(nameof(placementFactory));
            HandleChanged = true;
            PlacementChanged = true;
            _placement = _placementFactory(config);
        }

        public T Config { get; private set; }

        public IntPtr WindowHandle { get; private set; }

        public WindowPlacement Placement => _placement;

        public bool HandleChanged { get; private set; }

        public bool PlacementChanged { get; private set; }

        public void Update(T config, IntPtr handle)
        {
            Config = config;
            var next = _placementFactory(config);
            PlacementChanged = !_placement.Equals(next);
            _placement = next;

            if (WindowHandle != handle)
            {
                WindowHandle = handle;
                HandleChanged = true;
            }
        }

        public void ResetChangeFlags()
        {
            HandleChanged = false;
            PlacementChanged = false;
        }
    }

    private readonly record struct WindowReapplyInfo(string Id, string Kind, IntPtr Handle, WindowPlacement Placement);

    private readonly record struct WindowPlacement(WindowConfig Window, string MonitorStableId, string? ZonePresetId);

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

    private static ILogger ForEvent(string eventId) => Logger.ForContext("EventId", eventId);
}
