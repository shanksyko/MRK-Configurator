using System;
using System.Collections.Generic;
using System.Linq;
using Mieruka.Core.Layouts;

namespace Mieruka.Core.Services;

/// <summary>
/// Provides CRUD operations for zone presets.
/// </summary>
public sealed class ZonePresetService
{
    private readonly List<ZonePreset> _presets;
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ZonePresetService"/> class.
    /// </summary>
    /// <param name="presets">Initial preset collection.</param>
    public ZonePresetService(IList<ZonePreset>? presets = null)
    {
        _presets = presets switch
        {
            null => new List<ZonePreset>(),
            List<ZonePreset> list => list,
            _ => new List<ZonePreset>(presets),
        };

        EnsureUniquePresetIdentifiers(_presets);
    }

    /// <summary>
    /// Retrieves all known presets.
    /// </summary>
    public IReadOnlyList<ZonePreset> GetAll()
    {
        lock (_gate)
        {
            return _presets.ToArray();
        }
    }

    /// <summary>
    /// Searches for a preset using its identifier.
    /// </summary>
    /// <param name="id">Preset identifier.</param>
    /// <returns>The matching preset or <c>null</c> when not found.</returns>
    public ZonePreset? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Preset identifier cannot be null or whitespace.", nameof(id));
        }

        lock (_gate)
        {
            return _presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Adds a new preset to the collection.
    /// </summary>
    /// <param name="preset">Preset to add.</param>
    /// <returns><c>true</c> when the preset was added, or <c>false</c> if an entry with the same identifier already exists.</returns>
    public bool Create(ZonePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (_gate)
        {
            if (_presets.Any(existing => string.Equals(existing.Id, preset.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _presets.Add(preset);
            return true;
        }
    }

    /// <summary>
    /// Updates an existing preset.
    /// </summary>
    /// <param name="preset">Preset to update.</param>
    /// <returns><c>true</c> when the preset existed and was updated; otherwise, <c>false</c>.</returns>
    public bool Update(ZonePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (_gate)
        {
            for (var index = 0; index < _presets.Count; index++)
            {
                if (string.Equals(_presets[index].Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _presets[index] = preset;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Removes a preset by its identifier.
    /// </summary>
    /// <param name="id">Identifier of the preset to remove.</param>
    /// <returns><c>true</c> when the preset existed and was removed; otherwise, <c>false</c>.</returns>
    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Preset identifier cannot be null or whitespace.", nameof(id));
        }

        lock (_gate)
        {
            var preset = _presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                return false;
            }

            _presets.Remove(preset);
            return true;
        }
    }

    /// <summary>
    /// Replaces the current presets with a new collection.
    /// </summary>
    /// <param name="presets">Collection of presets to store.</param>
    public void ReplaceAll(IEnumerable<ZonePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var sanitized = presets
            .Select(static preset => preset ?? throw new ArgumentException("Preset collection cannot contain null entries.", nameof(presets)))
            .ToList();

        EnsureUniquePresetIdentifiers(sanitized);

        lock (_gate)
        {
            _presets.Clear();
            _presets.AddRange(sanitized);
        }
    }

    private static void EnsureUniquePresetIdentifiers(IList<ZonePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < presets.Count; index++)
        {
            var preset = presets[index] ?? throw new ArgumentException($"Preset at index {index} cannot be null.", nameof(presets));

            if (!identifiers.Add(preset.Id))
            {
                throw new ArgumentException($"Duplicate preset identifier '{preset.Id}'.", nameof(presets));
            }
        }
    }
}
