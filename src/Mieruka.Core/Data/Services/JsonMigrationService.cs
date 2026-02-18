using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Mapping;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Migra dados existentes de arquivos JSON para o banco SQLite unificado.
/// Lida com GeneralConfig + perfis avulsos em <c>%LocalAppData%/Mieruka/</c>.
/// A migração é idempotente — registros com ExternalId já existente são ignorados.
/// </summary>
public sealed class JsonMigrationService
{
    private static readonly ILogger Logger = Log.ForContext<JsonMigrationService>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly MierukaDbContext _context;

    public JsonMigrationService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Executa a migração completa: GeneralConfig → Applications, Sites, Monitors, CycleItems, AppSettings
    /// e cada arquivo .json em profiles/ → Profiles.
    /// </summary>
    public async Task MigrateAllAsync(CancellationToken ct = default)
    {
        Logger.Information("Iniciando migração JSON → SQLite.");

        var baseDir = GetMierukaBaseDirectory();

        await MigrateGeneralConfigAsync(baseDir, ct);
        await MigrateProfilesAsync(baseDir, ct);

        Logger.Information("Migração JSON → SQLite concluída.");
    }

    // ── GeneralConfig ──────────────────────────────────────────

    private async Task MigrateGeneralConfigAsync(string baseDir, CancellationToken ct)
    {
        // O arquivo de config pode estar em diferentes locais
        var candidates = new[]
        {
            Path.Combine(baseDir, "Configurator", "config.json"),
            Path.Combine(baseDir, "config.json"),
            Path.Combine(baseDir, "Configurator", "appsettings.json"),
        };

        string? configPath = candidates.FirstOrDefault(File.Exists);
        if (configPath is null)
        {
            Logger.Warning("Nenhum arquivo de configuração JSON encontrado em {BaseDir}.", baseDir);
            return;
        }

        Logger.Information("Migrando GeneralConfig de {Path}.", configPath);

        GeneralConfig? config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);

            // O JSON pode ser um objeto raiz com "GeneralConfig" ou o próprio GeneralConfig
            config = TryDeserializeConfig(json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao ler/deserializar {Path}.", configPath);
            return;
        }

        if (config is null)
        {
            Logger.Warning("GeneralConfig deserializado como null.");
            return;
        }

        // Applications
        foreach (var app in config.Applications)
        {
            if (await _context.Applications.AnyAsync(a => a.ExternalId == app.Id, ct))
                continue;
            _context.Applications.Add(EntityMapper.ToEntity(app));
        }

        // Sites
        foreach (var site in config.Sites)
        {
            if (await _context.Sites.AnyAsync(s => s.ExternalId == site.Id, ct))
                continue;
            _context.Sites.Add(EntityMapper.ToEntity(site));
        }

        // Monitors
        foreach (var monitor in config.Monitors)
        {
            var stableId = monitor.StableId;
            if (string.IsNullOrEmpty(stableId)) stableId = monitor.DeviceName;
            if (await _context.Monitors.AnyAsync(m => m.StableId == stableId, ct))
                continue;
            _context.Monitors.Add(EntityMapper.ToEntity(monitor));
        }

        // CycleItems
        foreach (var item in config.Cycle.Items)
        {
            if (await _context.CycleItems.AnyAsync(c => c.ExternalId == item.Id, ct))
                continue;
            _context.CycleItems.Add(EntityMapper.ToEntity(item));
        }

        // AppSettings (CycleConfig, BrowserArguments, UpdateConfig)
        await UpsertSettingAsync("CycleConfig", new
        {
            config.Cycle.Enabled,
            config.Cycle.DefaultDurationSeconds,
            config.Cycle.Shuffle,
            config.Cycle.Hotkeys,
        }, ct);

        await UpsertSettingAsync("BrowserArguments", config.BrowserArguments, ct);
        await UpsertSettingAsync("UpdateConfig", config.AutoUpdate, ct);
        await UpsertSettingAsync("SchemaVersion", config.SchemaVersion, ct);

        await _context.SaveChangesAsync(ct);
        Logger.Information("GeneralConfig migrado: {Apps} apps, {Sites} sites, {Monitors} monitores.",
            config.Applications.Count, config.Sites.Count, config.Monitors.Count);
    }

    // ── Profiles ───────────────────────────────────────────────

    private async Task MigrateProfilesAsync(string baseDir, CancellationToken ct)
    {
        var profilesDir = Path.Combine(baseDir, "profiles");
        if (!Directory.Exists(profilesDir))
        {
            Logger.Debug("Diretório de profiles não encontrado: {Dir}.", profilesDir);
            return;
        }

        var files = Directory.GetFiles(profilesDir, "*.json");
        Logger.Information("Encontrados {Count} arquivo(s) de profile para migrar.", files.Length);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var doc = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions);

                if (doc?.Profile is null) continue;
                if (await _context.Profiles.AnyAsync(p => p.ExternalId == doc.Profile.Id, ct))
                    continue;

                _context.Profiles.Add(EntityMapper.ToEntity(doc.Profile));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Erro ao migrar profile de {File}.", file);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    // ── Helpers ────────────────────────────────────────────────

    private async Task UpsertSettingAsync(string key, object value, CancellationToken ct)
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

    private static GeneralConfig? TryDeserializeConfig(string json)
    {
        try
        {
            // Tenta como wrapper { "GeneralConfig": { ... } }
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("GeneralConfig", out var inner))
            {
                return JsonSerializer.Deserialize<GeneralConfig>(inner.GetRawText(), JsonOptions);
            }
        }
        catch { /* fallthrough */ }

        // Tenta diretamente como GeneralConfig
        return JsonSerializer.Deserialize<GeneralConfig>(json, JsonOptions);
    }

    private static string GetMierukaBaseDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Mieruka");
    }

    // ── Tipo interno para desserializar profiles ──

    private sealed class ProfileDocument
    {
        public string SchemaVersion { get; set; } = "1.0";
        public ProfileConfig? Profile { get; set; }
    }
}
