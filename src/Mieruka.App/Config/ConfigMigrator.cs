using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mieruka.Core.Models;

namespace Mieruka.App.Config;

/// <summary>
/// Applies schema migrations to <see cref="GeneralConfig"/> instances while
/// providing helpers for importing and exporting JSON payloads.
/// </summary>
internal sealed class ConfigMigrator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Migrates a configuration to the latest schema version.
    /// </summary>
    /// <param name="config">Configuration that should be migrated.</param>
    /// <returns>The migrated configuration.</returns>
    public GeneralConfig Migrate(GeneralConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var version = ResolveSourceVersion(config);
        var current = config;

        if (string.Equals(version, ConfigSchemaVersion.V1, StringComparison.OrdinalIgnoreCase))
        {
            current = UpgradeFromV1(current);
            version = ConfigSchemaVersion.V2;
        }

        if (!string.Equals(version, ConfigSchemaVersion.Latest, StringComparison.OrdinalIgnoreCase))
        {
            current = current with { SchemaVersion = ConfigSchemaVersion.Latest };
        }

        if (current.LegacyVersion is not null)
        {
            current = current with { LegacyVersion = null };
        }

        return current;
    }

    /// <summary>
    /// Imports a configuration from a JSON file applying migrations when necessary.
    /// </summary>
    /// <param name="filePath">Source file path.</param>
    /// <returns>The migrated configuration.</returns>
    public GeneralConfig ImportFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.OpenRead(filePath);
        return Import(stream);
    }

    /// <summary>
    /// Imports a configuration from a JSON stream applying migrations when necessary.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <returns>The migrated configuration.</returns>
    public GeneralConfig Import(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        var config = JsonSerializer.Deserialize<GeneralConfig>(stream, SerializerOptions) ?? new GeneralConfig();
        return Migrate(config);
    }

    /// <summary>
    /// Exports a configuration to a JSON file using the latest schema version.
    /// </summary>
    /// <param name="filePath">Target file path.</param>
    /// <param name="config">Configuration that should be exported.</param>
    public void ExportToFile(string filePath, GeneralConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        Export(stream, config);
    }

    /// <summary>
    /// Exports a configuration to a JSON stream using the latest schema version.
    /// </summary>
    /// <param name="stream">Target stream.</param>
    /// <param name="config">Configuration that should be exported.</param>
    public void Export(Stream stream, GeneralConfig config)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(config);

        var sanitized = Migrate(config);

        if (stream.CanSeek)
        {
            stream.SetLength(0);
            stream.Seek(0, SeekOrigin.Begin);
        }

        JsonSerializer.Serialize(stream, sanitized, SerializerOptions);
        stream.Flush();
    }

    private static string ResolveSourceVersion(GeneralConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SchemaVersion))
        {
            return config.SchemaVersion;
        }

        if (!string.IsNullOrWhiteSpace(config.LegacyVersion))
        {
            return config.LegacyVersion;
        }

        return ConfigSchemaVersion.V1;
    }

    private static GeneralConfig UpgradeFromV1(GeneralConfig config)
    {
        return config with
        {
            SchemaVersion = ConfigSchemaVersion.V2,
            LegacyVersion = null,
        };
    }
}
