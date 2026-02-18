namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade persistida de monitor (equivale a <see cref="Models.MonitorInfo"/>).
/// </summary>
public sealed class MonitorEntity
{
    public int Id { get; set; }
    public string StableId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    // Bounds
    public int BoundsX { get; set; }
    public int BoundsY { get; set; }
    public int BoundsWidth { get; set; }
    public int BoundsHeight { get; set; }

    // WorkArea
    public int WorkAreaX { get; set; }
    public int WorkAreaY { get; set; }
    public int WorkAreaWidth { get; set; }
    public int WorkAreaHeight { get; set; }

    public double Scale { get; set; } = 1.0;
    public string Orientation { get; set; } = "Unknown";
    public int Rotation { get; set; }
    public bool IsPrimary { get; set; }
    public string Connector { get; set; } = string.Empty;
    public string Edid { get; set; } = string.Empty;

    // MonitorKey
    public string KeyDeviceId { get; set; } = string.Empty;
    public int KeyDisplayIndex { get; set; }
    public int KeyAdapterLuidHigh { get; set; }
    public int KeyAdapterLuidLow { get; set; }
    public int KeyTargetId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
