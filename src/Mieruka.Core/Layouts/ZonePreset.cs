using System;
using System.Collections.Generic;
using System.Linq;

namespace Mieruka.Core.Layouts;

/// <summary>
/// Anchor point used to place zones inside a preset layout.
/// </summary>
public enum ZoneAnchor
{
    /// <summary>
    /// Anchor the zone to the top-left corner.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Anchor the zone to the top-center edge.
    /// </summary>
    TopCenter,

    /// <summary>
    /// Anchor the zone to the top-right corner.
    /// </summary>
    TopRight,

    /// <summary>
    /// Anchor the zone to the center-left edge.
    /// </summary>
    CenterLeft,

    /// <summary>
    /// Anchor the zone to the center of the layout.
    /// </summary>
    Center,

    /// <summary>
    /// Anchor the zone to the center-right edge.
    /// </summary>
    CenterRight,

    /// <summary>
    /// Anchor the zone to the bottom-left corner.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Anchor the zone to the bottom-center edge.
    /// </summary>
    BottomCenter,

    /// <summary>
    /// Anchor the zone to the bottom-right corner.
    /// </summary>
    BottomRight,
}

/// <summary>
/// Represents a named layout with one or more screen zones expressed as percentages.
/// </summary>
public sealed record class ZonePreset
{
    private static readonly IReadOnlyList<ZonePreset> DefaultPresets = CreateDefaults();

    /// <summary>
    /// Initializes a new instance of the <see cref="ZonePreset"/> class.
    /// </summary>
    /// <param name="id">Unique identifier of the preset.</param>
    /// <param name="name">Display name for the preset.</param>
    /// <param name="zones">Collection of zones that compose the preset.</param>
    /// <param name="description">Optional preset description.</param>
    public ZonePreset(string id, string name, IEnumerable<Zone> zones, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Preset identifier cannot be null or whitespace.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name cannot be null or whitespace.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(zones);

        var zoneArray = zones
            .Select(zone => zone ?? throw new ArgumentException("Zone entries cannot be null.", nameof(zones)))
            .ToArray();

        if (zoneArray.Length == 0)
        {
            throw new ArgumentException("Preset must define at least one zone.", nameof(zones));
        }

        var duplicate = zoneArray
            .GroupBy(z => z.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate zone identifier '{duplicate.Key}'.", nameof(zones));
        }

        Id = id;
        Name = name;
        Description = description ?? string.Empty;
        Zones = Array.AsReadOnly(zoneArray);
    }

    /// <summary>
    /// Gets the unique identifier of the preset.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the friendly name of the preset.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the description of the preset.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the collection of zones that define the preset.
    /// </summary>
    public IReadOnlyList<Zone> Zones { get; }

    /// <summary>
    /// Gets the default presets shipped with the application.
    /// </summary>
    public static IReadOnlyList<ZonePreset> Defaults => DefaultPresets;

    private static IReadOnlyList<ZonePreset> CreateDefaults()
    {
        const double twoColumnsWidth = 50d;
        const double twoByTwoWidth = 50d;
        const double twoByTwoHeight = 50d;
        const double threeColumnsWidth = 33.3333d;

        var presets = new[]
        {
            new ZonePreset(
                "full",
                "Full",
                new[]
                {
                    new Zone("main", 0d, 0d, 100d, 100d),
                },
                "Single zone covering the entire layout."),
            new ZonePreset(
                "two-columns",
                "2 colunas",
                new[]
                {
                    new Zone("left", 0d, 0d, twoColumnsWidth, 100d),
                    new Zone("right", twoColumnsWidth, 0d, twoColumnsWidth, 100d),
                },
                "Two equally sized vertical columns."),
            new ZonePreset(
                "three-columns",
                "3 colunas",
                new[]
                {
                    new Zone("left", 0d, 0d, threeColumnsWidth, 100d),
                    new Zone("center", threeColumnsWidth, 0d, threeColumnsWidth, 100d),
                    new Zone("right", threeColumnsWidth * 2, 0d, 100d - (threeColumnsWidth * 2), 100d),
                },
                "Three vertical columns."),
            new ZonePreset(
                "two-by-two",
                "2x2",
                new[]
                {
                    new Zone("top-left", 0d, 0d, twoByTwoWidth, twoByTwoHeight),
                    new Zone("top-right", twoByTwoWidth, 0d, twoByTwoWidth, twoByTwoHeight),
                    new Zone("bottom-left", 0d, twoByTwoHeight, twoByTwoWidth, twoByTwoHeight),
                    new Zone("bottom-right", twoByTwoWidth, twoByTwoHeight, twoByTwoWidth, twoByTwoHeight),
                },
                "Four quadrants in a 2x2 grid."),
        };

        return Array.AsReadOnly(presets);
    }

    /// <summary>
    /// Represents a rectangular zone within a preset, measured in percentages.
    /// </summary>
    public sealed record class Zone
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Zone"/> class.
        /// </summary>
        /// <param name="id">Unique identifier of the zone.</param>
        /// <param name="leftPercent">Horizontal offset from the left edge in percent.</param>
        /// <param name="topPercent">Vertical offset from the top edge in percent.</param>
        /// <param name="widthPercent">Width of the zone in percent.</param>
        /// <param name="heightPercent">Height of the zone in percent.</param>
        /// <param name="anchor">Anchor point used to place the zone.</param>
        public Zone(string id, double leftPercent, double topPercent, double widthPercent, double heightPercent, ZoneAnchor anchor = ZoneAnchor.TopLeft)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Zone identifier cannot be null or whitespace.", nameof(id));
            }

            Id = id;
            LeftPercentage = ValidatePercentage(leftPercent, nameof(leftPercent));
            TopPercentage = ValidatePercentage(topPercent, nameof(topPercent));
            WidthPercentage = ValidateSize(widthPercent, nameof(widthPercent));
            HeightPercentage = ValidateSize(heightPercent, nameof(heightPercent));
            Anchor = anchor;
        }

        /// <summary>
        /// Gets the unique identifier of the zone.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the horizontal offset in percentage from the left edge.
        /// </summary>
        public double LeftPercentage { get; }

        /// <summary>
        /// Gets the vertical offset in percentage from the top edge.
        /// </summary>
        public double TopPercentage { get; }

        /// <summary>
        /// Gets the width of the zone in percentage.
        /// </summary>
        public double WidthPercentage { get; }

        /// <summary>
        /// Gets the height of the zone in percentage.
        /// </summary>
        public double HeightPercentage { get; }

        /// <summary>
        /// Gets the anchor used to position the zone.
        /// </summary>
        public ZoneAnchor Anchor { get; }

        private static double ValidatePercentage(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Percentage must be a finite number.");
            }

            if (value < 0d || value > 100d)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Percentage must be between 0 and 100.");
            }

            return value;
        }

        private static double ValidateSize(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Size percentage must be a finite number.");
            }

            if (value <= 0d || value > 100d)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Size percentage must be greater than 0 and less than or equal to 100.");
            }

            return value;
        }
    }
}
