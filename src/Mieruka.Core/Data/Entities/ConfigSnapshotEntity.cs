using System;

namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Stores a point-in-time snapshot of the application configuration for versioning and rollback.
/// </summary>
public sealed class ConfigSnapshotEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Human-readable label describing the snapshot (e.g., "Auto-save before import", "Manual snapshot").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON representation of the <see cref="Mieruka.Core.Models.GeneralConfig"/> at the time of capture.
    /// </summary>
    public string ConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the snapshot was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
