using System.Drawing;
using System.Text.Json;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Core.Data.Mapping;

/// <summary>
/// Mapeia entre os modelos de domínio (records imutáveis) e entidades do banco de dados.
/// </summary>
public static class EntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ════════════ AppConfig ↔ ApplicationEntity ════════════

    public static ApplicationEntity ToEntity(AppConfig app) => new()
    {
        ExternalId = app.Id,
        Name = app.Name,
        Order = app.Order,
        ExecutablePath = app.ExecutablePath,
        Arguments = app.Arguments,
        AutoStart = app.AutoStart,
        AskBeforeLaunch = app.AskBeforeLaunch,
        RequiresNetwork = app.RequiresNetwork,
        DelayMs = app.DelayMs,
        TargetMonitorStableId = app.TargetMonitorStableId,
        TargetZonePresetId = app.TargetZonePresetId,
        EnvironmentVariablesJson = JsonSerializer.Serialize(app.EnvironmentVariables, JsonOptions),
        WatchdogJson = JsonSerializer.Serialize(app.Watchdog, JsonOptions),
        WindowJson = JsonSerializer.Serialize(app.Window, JsonOptions),
    };

    public static AppConfig ToModel(ApplicationEntity entity) => new()
    {
        Id = entity.ExternalId,
        Name = entity.Name,
        Order = entity.Order,
        ExecutablePath = entity.ExecutablePath,
        Arguments = entity.Arguments,
        AutoStart = entity.AutoStart,
        AskBeforeLaunch = entity.AskBeforeLaunch,
        RequiresNetwork = entity.RequiresNetwork,
        DelayMs = entity.DelayMs,
        TargetMonitorStableId = entity.TargetMonitorStableId,
        TargetZonePresetId = entity.TargetZonePresetId,
        EnvironmentVariables = Deserialize<Dictionary<string, string>>(entity.EnvironmentVariablesJson)
                               ?? new Dictionary<string, string>(),
        Watchdog = Deserialize<WatchdogSettings>(entity.WatchdogJson) ?? new(),
        Window = Deserialize<WindowConfig>(entity.WindowJson) ?? new(),
    };

    // ════════════ SiteConfig ↔ SiteEntity ════════════

    public static SiteEntity ToEntity(SiteConfig site) => new()
    {
        ExternalId = site.Id,
        Url = site.Url,
        Browser = site.Browser.ToString(),
        UserDataDirectory = site.UserDataDirectory,
        ProfileDirectory = site.ProfileDirectory,
        AppMode = site.AppMode,
        KioskMode = site.KioskMode,
        ReloadOnActivate = site.ReloadOnActivate,
        ReloadIntervalSeconds = site.ReloadIntervalSeconds,
        TargetMonitorStableId = site.TargetMonitorStableId,
        TargetZonePresetId = site.TargetZonePresetId,
        BrowserArgumentsJson = JsonSerializer.Serialize(site.BrowserArguments, JsonOptions),
        HeadersJson = JsonSerializer.Serialize(site.Headers, JsonOptions),
        AllowedTabHostsJson = JsonSerializer.Serialize(site.AllowedTabHosts, JsonOptions),
        WatchdogJson = JsonSerializer.Serialize(site.Watchdog, JsonOptions),
        WindowJson = JsonSerializer.Serialize(site.Window, JsonOptions),
        LoginJson = site.Login is not null
            ? JsonSerializer.Serialize(site.Login, JsonOptions)
            : null,
    };

    public static SiteConfig ToModel(SiteEntity entity) => new()
    {
        Id = entity.ExternalId,
        Url = entity.Url,
        Browser = Enum.TryParse<BrowserType>(entity.Browser, true, out var bt)
            ? bt : BrowserType.Chrome,
        UserDataDirectory = entity.UserDataDirectory,
        ProfileDirectory = entity.ProfileDirectory,
        AppMode = entity.AppMode,
        KioskMode = entity.KioskMode,
        ReloadOnActivate = entity.ReloadOnActivate,
        ReloadIntervalSeconds = entity.ReloadIntervalSeconds,
        TargetMonitorStableId = entity.TargetMonitorStableId,
        TargetZonePresetId = entity.TargetZonePresetId,
        BrowserArguments = Deserialize<List<string>>(entity.BrowserArgumentsJson) ?? [],
        Headers = Deserialize<Dictionary<string, string>>(entity.HeadersJson)
                  ?? new Dictionary<string, string>(),
        AllowedTabHosts = Deserialize<List<string>>(entity.AllowedTabHostsJson) ?? [],
        Watchdog = Deserialize<WatchdogSettings>(entity.WatchdogJson) ?? new(),
        Window = Deserialize<WindowConfig>(entity.WindowJson) ?? new(),
        Login = !string.IsNullOrEmpty(entity.LoginJson)
            ? Deserialize<LoginProfile>(entity.LoginJson)
            : null,
    };

    // ════════════ MonitorInfo ↔ MonitorEntity ════════════

    public static MonitorEntity ToEntity(MonitorInfo monitor) => new()
    {
        StableId = monitor.StableId,
        Name = monitor.Name,
        DeviceName = monitor.DeviceName,
        Width = monitor.Width,
        Height = monitor.Height,
        BoundsX = monitor.Bounds.X,
        BoundsY = monitor.Bounds.Y,
        BoundsWidth = monitor.Bounds.Width,
        BoundsHeight = monitor.Bounds.Height,
        WorkAreaX = monitor.WorkArea.X,
        WorkAreaY = monitor.WorkArea.Y,
        WorkAreaWidth = monitor.WorkArea.Width,
        WorkAreaHeight = monitor.WorkArea.Height,
        Scale = monitor.Scale,
        Orientation = monitor.Orientation.ToString(),
        Rotation = monitor.Rotation,
        IsPrimary = monitor.IsPrimary,
        Connector = monitor.Connector,
        Edid = monitor.Edid,
        KeyDeviceId = monitor.Key.DeviceId,
        KeyDisplayIndex = monitor.Key.DisplayIndex,
        KeyAdapterLuidHigh = monitor.Key.AdapterLuidHigh,
        KeyAdapterLuidLow = monitor.Key.AdapterLuidLow,
        KeyTargetId = monitor.Key.TargetId,
    };

    public static MonitorInfo ToModel(MonitorEntity entity) => new()
    {
        StableId = entity.StableId,
        Name = entity.Name,
        DeviceName = entity.DeviceName,
        Width = entity.Width,
        Height = entity.Height,
        Bounds = new Rectangle(entity.BoundsX, entity.BoundsY, entity.BoundsWidth, entity.BoundsHeight),
        WorkArea = new Rectangle(entity.WorkAreaX, entity.WorkAreaY, entity.WorkAreaWidth, entity.WorkAreaHeight),
        Scale = entity.Scale,
        Orientation = Enum.TryParse<MonitorOrientation>(entity.Orientation, true, out var o)
            ? o : MonitorOrientation.Unknown,
        Rotation = entity.Rotation,
        IsPrimary = entity.IsPrimary,
        Connector = entity.Connector,
        Edid = entity.Edid,
        Key = new MonitorKey
        {
            DeviceId = entity.KeyDeviceId,
            DisplayIndex = entity.KeyDisplayIndex,
            AdapterLuidHigh = entity.KeyAdapterLuidHigh,
            AdapterLuidLow = entity.KeyAdapterLuidLow,
            TargetId = entity.KeyTargetId,
        },
    };

    // ════════════ ProfileConfig ↔ ProfileEntity ════════════

    public static ProfileEntity ToEntity(ProfileConfig profile) => new()
    {
        ExternalId = profile.Id,
        Name = profile.Name,
        SchemaVersion = profile.SchemaVersion,
        DefaultMonitorId = profile.DefaultMonitorId,
        ApplicationsJson = JsonSerializer.Serialize(profile.Applications, JsonOptions),
        WindowsJson = JsonSerializer.Serialize(profile.Windows, JsonOptions),
    };

    public static ProfileConfig ToModel(ProfileEntity entity) => new()
    {
        Id = entity.ExternalId,
        Name = entity.Name,
        SchemaVersion = entity.SchemaVersion,
        DefaultMonitorId = entity.DefaultMonitorId,
        Applications = Deserialize<List<AppConfig>>(entity.ApplicationsJson) ?? [],
        Windows = Deserialize<List<WindowConfig>>(entity.WindowsJson) ?? [],
    };

    // ════════════ CycleItem ↔ CycleItemEntity ════════════

    public static CycleItemEntity ToEntity(CycleItem item) => new()
    {
        ExternalId = item.Id,
        TargetId = item.TargetId,
        TargetType = item.TargetType,
        DurationSeconds = item.DurationSeconds,
        Enabled = item.Enabled,
    };

    public static CycleItem ToModel(CycleItemEntity entity) => new()
    {
        Id = entity.ExternalId,
        TargetId = entity.TargetId,
        TargetType = entity.TargetType,
        DurationSeconds = entity.DurationSeconds,
        Enabled = entity.Enabled,
    };

    // ════════════ Helper ════════════

    private static readonly ILogger Logger = Log.ForContext(typeof(EntityMapper));

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Falha ao deserializar JSON para {Type}: {Json}",
                typeof(T).Name, json?.Length > 200 ? json[..200] + "..." : json);
            return default;
        }
    }
}
