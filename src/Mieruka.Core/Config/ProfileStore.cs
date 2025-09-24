using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mieruka.Core.Models;

namespace Mieruka.Core.Config;

/// <summary>
/// Persists profile configurations using a JSON backed store with schema versioning.
/// </summary>
public sealed class ProfileStore
{
    /// <summary>
    /// Current schema version supported by the store.
    /// </summary>
    public const string CurrentSchemaVersion = "1.0";

    private static readonly ReadOnlyCollection<char> InvalidFileNameChars = Array.AsReadOnly(Path.GetInvalidFileNameChars());

    private readonly object _gate = new();
    private readonly string _profilesDirectory;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileStore"/> class.
    /// </summary>
    /// <param name="baseDirectory">Optional directory used to store the profiles.</param>
    public ProfileStore(string? baseDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesDirectory = baseDirectory ?? Path.Combine(localAppData, "Mieruka", "profiles");
        Directory.CreateDirectory(_profilesDirectory);
    }

    /// <summary>
    /// Saves the provided profile to the store.
    /// </summary>
    /// <param name="profile">Profile that should be persisted.</param>
    public void Save(ProfileConfig profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new ArgumentException("Profile identifier must not be empty.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Profile name must not be empty.", nameof(profile));
        }

        var sanitized = SanitizeProfile(profile);
        if (sanitized.SchemaVersion <= 0)
        {
            sanitized = sanitized with { SchemaVersion = 1 };
        }
        var document = new ProfileDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Profile = sanitized,
        };

        var path = ResolveProfilePath(sanitized.Id);

        lock (_gate)
        {
            Directory.CreateDirectory(_profilesDirectory);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, document, _serializerOptions);
        }
    }

    /// <summary>
    /// Loads a profile by its identifier.
    /// </summary>
    /// <param name="id">Identifier of the profile that should be loaded.</param>
    /// <returns>The profile when available; otherwise, <see langword="null"/>.</returns>
    public ProfileConfig? Load(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile identifier must not be empty.", nameof(id));
        }

        var path = ResolveProfilePath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<ProfileDocument>(stream, _serializerOptions);
            if (document is null)
            {
                return null;
            }

            if (!IsVersionSupported(document.SchemaVersion))
            {
                throw new NotSupportedException($"Unsupported profile schema version '{document.SchemaVersion}'.");
            }

            return SanitizeProfile(document.Profile);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists all profiles persisted in the store.
    /// </summary>
    /// <returns>A read-only list of profiles.</returns>
    public IReadOnlyList<ProfileConfig> ListAll()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            return Array.Empty<ProfileConfig>();
        }

        var profiles = new List<ProfileConfig>();

        foreach (var file in Directory.EnumerateFiles(_profilesDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var document = JsonSerializer.Deserialize<ProfileDocument>(stream, _serializerOptions);
                if (document is null || !IsVersionSupported(document.SchemaVersion))
                {
                    continue;
                }

                profiles.Add(SanitizeProfile(document.Profile));
            }
            catch
            {
                // Ignore malformed files.
            }
        }

        return profiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Exports all stored profiles to a single JSON file.
    /// </summary>
    /// <param name="filePath">Destination file path.</param>
    public void Export(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }

        var document = new ProfileCollectionDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Profiles = ListAll(),
        };

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, document, _serializerOptions);
    }

    /// <summary>
    /// Imports profiles from a JSON export file.
    /// </summary>
    /// <param name="filePath">Path to the export file.</param>
    /// <returns>Profiles that were imported.</returns>
    public IReadOnlyList<ProfileConfig> Import(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }

        using var stream = File.OpenRead(filePath);
        var document = JsonSerializer.Deserialize<ProfileCollectionDocument>(stream, _serializerOptions);
        if (document is null)
        {
            return Array.Empty<ProfileConfig>();
        }

        if (!IsVersionSupported(document.SchemaVersion))
        {
            throw new NotSupportedException($"Unsupported profile schema version '{document.SchemaVersion}'.");
        }

        var imported = new List<ProfileConfig>();
        foreach (var profile in document.Profiles)
        {
            Save(profile);
            imported.Add(SanitizeProfile(profile));
        }

        return imported;
    }

    private static ProfileConfig SanitizeProfile(ProfileConfig profile)
    {
        var applications = CloneApplications(profile.Applications);
        var windows = CloneWindows(profile.Windows);

        return profile with
        {
            Applications = applications,
            Windows = windows,
        };
    }

    private static IList<AppConfig> CloneApplications(IList<AppConfig> source)
    {
        var result = new List<AppConfig>(source.Count);

        foreach (var app in source)
        {
            if (app is null)
            {
                continue;
            }

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in app.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    environment[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            var watchdog = app.Watchdog with
            {
                HealthCheck = app.Watchdog.HealthCheck is null
                    ? null
                    : app.Watchdog.HealthCheck with { },
            };

            var window = app.Window with
            {
                Monitor = app.Window.Monitor with { },
            };

            result.Add(app with
            {
                EnvironmentVariables = environment,
                Watchdog = watchdog,
                Window = window,
            });
        }

        return result;
    }

    private static IList<WindowConfig> CloneWindows(IList<WindowConfig> source)
    {
        var result = new List<WindowConfig>(source.Count);

        foreach (var window in source)
        {
            if (window is null)
            {
                continue;
            }

            result.Add(window with
            {
                Monitor = window.Monitor with { },
            });
        }

        return result;
    }

    private static bool IsVersionSupported(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return true;
        }

        return string.Equals(schemaVersion, CurrentSchemaVersion, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveProfilePath(string id)
    {
        var fileName = GetSafeFileName(id);
        return Path.Combine(_profilesDirectory, fileName + ".json");
    }

    private static string GetSafeFileName(string id)
    {
        var trimmed = (id ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "profile";
        }

        Span<char> buffer = trimmed.Length <= 256 ? stackalloc char[trimmed.Length] : new char[trimmed.Length];
        var index = 0;

        foreach (var ch in trimmed)
        {
            if (!InvalidFileNameChars.Contains(ch))
            {
                buffer[index++] = ch;
            }
        }

        if (index == 0)
        {
            return "profile";
        }

        var cleaned = new string(buffer[..index]);
        return cleaned.Length == 0 ? "profile" : cleaned;
    }

    private sealed record class ProfileDocument
    {
        public string SchemaVersion { get; init; } = CurrentSchemaVersion;

        public ProfileConfig Profile { get; init; } = new();
    }

    private sealed record class ProfileCollectionDocument
    {
        public string SchemaVersion { get; init; } = CurrentSchemaVersion;

        public IReadOnlyList<ProfileConfig> Profiles { get; init; } = Array.Empty<ProfileConfig>();
    }
}
