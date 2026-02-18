using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Mapping;
using Mieruka.Core.Data.Repositories;
using Mieruka.Core.Models;
using System.Text.Json;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Serviço CRUD para configuração de domínio (Applications, Sites, Monitors, Profiles, CycleItems).
/// Provê acesso ao banco SQLite como substituto dos arquivos JSON.
/// </summary>
public sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly MierukaDbContext _context;
    private readonly IRepository<ApplicationEntity> _appRepo;
    private readonly IRepository<SiteEntity> _siteRepo;
    private readonly IRepository<MonitorEntity> _monitorRepo;
    private readonly IRepository<ProfileEntity> _profileRepo;
    private readonly IRepository<CycleItemEntity> _cycleRepo;

    public ConfigurationService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _appRepo = new Repository<ApplicationEntity>(context);
        _siteRepo = new Repository<SiteEntity>(context);
        _monitorRepo = new Repository<MonitorEntity>(context);
        _profileRepo = new Repository<ProfileEntity>(context);
        _cycleRepo = new Repository<CycleItemEntity>(context);
    }

    // ────────────────── Applications ──────────────────

    public async Task<IReadOnlyList<AppConfig>> GetAllAppsAsync(CancellationToken ct = default)
    {
        var entities = await _appRepo.GetAllAsync(ct);
        return entities.Select(EntityMapper.ToModel).ToList();
    }

    public async Task<AppConfig?> GetAppByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var entities = await _appRepo.FindAsync(a => a.ExternalId == externalId, ct);
        return entities.Select(EntityMapper.ToModel).FirstOrDefault();
    }

    public async Task SaveAppAsync(AppConfig app, CancellationToken ct = default)
    {
        var existing = await _context.Applications
            .FirstOrDefaultAsync(a => a.ExternalId == app.Id, ct);

        if (existing is not null)
        {
            var updated = EntityMapper.ToEntity(app);
            existing.Name = updated.Name;
            existing.Order = updated.Order;
            existing.ExecutablePath = updated.ExecutablePath;
            existing.Arguments = updated.Arguments;
            existing.AutoStart = updated.AutoStart;
            existing.AskBeforeLaunch = updated.AskBeforeLaunch;
            existing.RequiresNetwork = updated.RequiresNetwork;
            existing.DelayMs = updated.DelayMs;
            existing.TargetMonitorStableId = updated.TargetMonitorStableId;
            existing.TargetZonePresetId = updated.TargetZonePresetId;
            existing.EnvironmentVariablesJson = updated.EnvironmentVariablesJson;
            existing.WatchdogJson = updated.WatchdogJson;
            existing.WindowJson = updated.WindowJson;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entity = EntityMapper.ToEntity(app);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            _context.Applications.Add(entity);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAppAsync(string externalId, CancellationToken ct = default)
    {
        var entity = await _context.Applications
            .FirstOrDefaultAsync(a => a.ExternalId == externalId, ct);
        if (entity is not null)
        {
            _context.Applications.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }

    // ────────────────── Sites ──────────────────

    public async Task<IReadOnlyList<SiteConfig>> GetAllSitesAsync(CancellationToken ct = default)
    {
        var entities = await _siteRepo.GetAllAsync(ct);
        return entities.Select(EntityMapper.ToModel).ToList();
    }

    public async Task<SiteConfig?> GetSiteByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var entities = await _siteRepo.FindAsync(s => s.ExternalId == externalId, ct);
        return entities.Select(EntityMapper.ToModel).FirstOrDefault();
    }

    public async Task SaveSiteAsync(SiteConfig site, CancellationToken ct = default)
    {
        var existing = await _context.Sites
            .FirstOrDefaultAsync(s => s.ExternalId == site.Id, ct);

        if (existing is not null)
        {
            var updated = EntityMapper.ToEntity(site);
            existing.Url = updated.Url;
            existing.Browser = updated.Browser;
            existing.UserDataDirectory = updated.UserDataDirectory;
            existing.ProfileDirectory = updated.ProfileDirectory;
            existing.AppMode = updated.AppMode;
            existing.KioskMode = updated.KioskMode;
            existing.ReloadOnActivate = updated.ReloadOnActivate;
            existing.ReloadIntervalSeconds = updated.ReloadIntervalSeconds;
            existing.TargetMonitorStableId = updated.TargetMonitorStableId;
            existing.TargetZonePresetId = updated.TargetZonePresetId;
            existing.BrowserArgumentsJson = updated.BrowserArgumentsJson;
            existing.HeadersJson = updated.HeadersJson;
            existing.AllowedTabHostsJson = updated.AllowedTabHostsJson;
            existing.WatchdogJson = updated.WatchdogJson;
            existing.WindowJson = updated.WindowJson;
            existing.LoginJson = updated.LoginJson;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entity = EntityMapper.ToEntity(site);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            _context.Sites.Add(entity);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteSiteAsync(string externalId, CancellationToken ct = default)
    {
        var entity = await _context.Sites
            .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);
        if (entity is not null)
        {
            _context.Sites.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }

    // ────────────────── Monitors ──────────────────

    public async Task<IReadOnlyList<MonitorInfo>> GetAllMonitorsAsync(CancellationToken ct = default)
    {
        var entities = await _monitorRepo.GetAllAsync(ct);
        return entities.Select(EntityMapper.ToModel).ToList();
    }

    public async Task SaveMonitorAsync(MonitorInfo monitor, CancellationToken ct = default)
    {
        var stableId = !string.IsNullOrEmpty(monitor.StableId) ? monitor.StableId : monitor.DeviceName;
        var existing = await _context.Monitors
            .FirstOrDefaultAsync(m => m.StableId == stableId, ct);

        if (existing is not null)
        {
            var updated = EntityMapper.ToEntity(monitor);
            existing.Name = updated.Name;
            existing.DeviceName = updated.DeviceName;
            existing.Width = updated.Width;
            existing.Height = updated.Height;
            existing.BoundsX = updated.BoundsX;
            existing.BoundsY = updated.BoundsY;
            existing.BoundsWidth = updated.BoundsWidth;
            existing.BoundsHeight = updated.BoundsHeight;
            existing.WorkAreaX = updated.WorkAreaX;
            existing.WorkAreaY = updated.WorkAreaY;
            existing.WorkAreaWidth = updated.WorkAreaWidth;
            existing.WorkAreaHeight = updated.WorkAreaHeight;
            existing.Scale = updated.Scale;
            existing.Orientation = updated.Orientation;
            existing.Rotation = updated.Rotation;
            existing.IsPrimary = updated.IsPrimary;
            existing.Connector = updated.Connector;
            existing.Edid = updated.Edid;
            existing.KeyDeviceId = updated.KeyDeviceId;
            existing.KeyDisplayIndex = updated.KeyDisplayIndex;
            existing.KeyAdapterLuidHigh = updated.KeyAdapterLuidHigh;
            existing.KeyAdapterLuidLow = updated.KeyAdapterLuidLow;
            existing.KeyTargetId = updated.KeyTargetId;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entity = EntityMapper.ToEntity(monitor);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            _context.Monitors.Add(entity);
        }

        await _context.SaveChangesAsync(ct);
    }

    // ────────────────── Profiles ──────────────────

    public async Task<IReadOnlyList<ProfileConfig>> GetAllProfilesAsync(CancellationToken ct = default)
    {
        var entities = await _profileRepo.GetAllAsync(ct);
        return entities.Select(EntityMapper.ToModel).ToList();
    }

    public async Task<ProfileConfig?> GetProfileByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var entities = await _profileRepo.FindAsync(p => p.ExternalId == externalId, ct);
        return entities.Select(EntityMapper.ToModel).FirstOrDefault();
    }

    public async Task SaveProfileAsync(ProfileConfig profile, CancellationToken ct = default)
    {
        var existing = await _context.Profiles
            .FirstOrDefaultAsync(p => p.ExternalId == profile.Id, ct);

        if (existing is not null)
        {
            var updated = EntityMapper.ToEntity(profile);
            existing.Name = updated.Name;
            existing.SchemaVersion = updated.SchemaVersion;
            existing.DefaultMonitorId = updated.DefaultMonitorId;
            existing.ApplicationsJson = updated.ApplicationsJson;
            existing.WindowsJson = updated.WindowsJson;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entity = EntityMapper.ToEntity(profile);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            _context.Profiles.Add(entity);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteProfileAsync(string externalId, CancellationToken ct = default)
    {
        var entity = await _context.Profiles
            .FirstOrDefaultAsync(p => p.ExternalId == externalId, ct);
        if (entity is not null)
        {
            _context.Profiles.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }

    // ────────────────── GeneralConfig (reconstrói a partir do banco) ──────────────────

    /// <summary>
    /// Reconstrói um <see cref="GeneralConfig"/> completo a partir das tabelas do banco.
    /// </summary>
    public async Task<GeneralConfig> LoadGeneralConfigAsync(CancellationToken ct = default)
    {
        var apps = await GetAllAppsAsync(ct);
        var sites = await GetAllSitesAsync(ct);
        var monitors = await GetAllMonitorsAsync(ct);
        var cycleEntities = await _cycleRepo.GetAllAsync(ct);
        var cycleItems = cycleEntities.Select(EntityMapper.ToModel).ToList();

        var cycleConfig = await GetSettingAsync<CycleSettingsDto>("CycleConfig", ct);
        var browserArgs = await GetSettingAsync<BrowserArgumentsSettings>("BrowserArguments", ct);
        var updateConfig = await GetSettingAsync<UpdateConfig>("UpdateConfig", ct);

        return new GeneralConfig
        {
            SchemaVersion = ConfigSchemaVersion.Latest,
            Monitors = monitors.ToList(),
            Applications = apps.ToList(),
            Sites = sites.ToList(),
            BrowserArguments = browserArgs ?? new BrowserArgumentsSettings(),
            Cycle = new CycleConfig
            {
                Enabled = cycleConfig?.Enabled ?? true,
                DefaultDurationSeconds = cycleConfig?.DefaultDurationSeconds ?? 60,
                Shuffle = cycleConfig?.Shuffle ?? false,
                Items = cycleItems,
                Hotkeys = cycleConfig?.Hotkeys ?? new CycleHotkeyConfig(),
            },
            AutoUpdate = updateConfig ?? new UpdateConfig(),
        };
    }

    /// <summary>
    /// Salva um <see cref="GeneralConfig"/> completo no banco (decompondo em tabelas).
    /// </summary>
    public async Task SaveGeneralConfigAsync(GeneralConfig config, CancellationToken ct = default)
    {
        // Apps
        var existingAppIds = await _context.Applications.Select(a => a.ExternalId).ToListAsync(ct);
        var newAppIds = config.Applications.Select(a => a.Id).ToHashSet();

        // Remove apps que não estão mais na config
        var appsToRemove = await _context.Applications
            .Where(a => !newAppIds.Contains(a.ExternalId)).ToListAsync(ct);
        _context.Applications.RemoveRange(appsToRemove);

        foreach (var app in config.Applications)
            await SaveAppAsync(app, ct);

        // Sites
        var newSiteIds = config.Sites.Select(s => s.Id).ToHashSet();
        var sitesToRemove = await _context.Sites
            .Where(s => !newSiteIds.Contains(s.ExternalId)).ToListAsync(ct);
        _context.Sites.RemoveRange(sitesToRemove);

        foreach (var site in config.Sites)
            await SaveSiteAsync(site, ct);

        // Monitors
        foreach (var monitor in config.Monitors)
            await SaveMonitorAsync(monitor, ct);

        // CycleItems
        var newCycleIds = config.Cycle.Items.Select(i => i.Id).ToHashSet();
        var cyclesToRemove = await _context.CycleItems
            .Where(c => !newCycleIds.Contains(c.ExternalId)).ToListAsync(ct);
        _context.CycleItems.RemoveRange(cyclesToRemove);

        foreach (var item in config.Cycle.Items)
        {
            var existing = await _context.CycleItems
                .FirstOrDefaultAsync(c => c.ExternalId == item.Id, ct);
            if (existing is not null)
            {
                var updated = EntityMapper.ToEntity(item);
                existing.TargetId = updated.TargetId;
                existing.TargetType = updated.TargetType;
                existing.DurationSeconds = updated.DurationSeconds;
                existing.Enabled = updated.Enabled;
            }
            else
            {
                _context.CycleItems.Add(EntityMapper.ToEntity(item));
            }
        }

        // Settings
        await UpsertSettingAsync("CycleConfig", new CycleSettingsDto
        {
            Enabled = config.Cycle.Enabled,
            DefaultDurationSeconds = config.Cycle.DefaultDurationSeconds,
            Shuffle = config.Cycle.Shuffle,
            Hotkeys = config.Cycle.Hotkeys,
        }, ct);

        await UpsertSettingAsync("BrowserArguments", config.BrowserArguments, ct);
        await UpsertSettingAsync("UpdateConfig", config.AutoUpdate, ct);
        await UpsertSettingAsync("SchemaVersion", config.SchemaVersion, ct);

        await _context.SaveChangesAsync(ct);
    }

    // ────────────────── Settings helpers ──────────────────

    public async Task<T?> GetSettingAsync<T>(string key, CancellationToken ct = default)
    {
        var entity = await _context.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        if (entity is null) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(entity.ValueJson, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    public async Task UpsertSettingAsync(string key, object value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is not null)
        {
            existing.ValueJson = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.AppSettings.Add(new AppSettingEntity
            {
                Key = key,
                ValueJson = json,
                UpdatedAt = DateTime.UtcNow,
            });
        }
    }

    // ── DTO interno para armazenar settings do CycleConfig ──

    private sealed class CycleSettingsDto
    {
        public bool Enabled { get; set; } = true;
        public int DefaultDurationSeconds { get; set; } = 60;
        public bool Shuffle { get; set; }
        public CycleHotkeyConfig Hotkeys { get; set; } = new();
    }
}
