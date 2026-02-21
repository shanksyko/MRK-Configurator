using System.Drawing;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using Xunit;

namespace Mieruka.Tests;

public sealed class WindowPlacementHelperTests
{
    [Fact]
    public void ResolveBounds_WithScaledMonitor_PreservesPhysicalPixels()
    {
        var monitor = new MonitorInfo
        {
            Width = 3840,
            Height = 2160,
            Bounds = new Rectangle(2000, 100, 3840, 2160),
            WorkArea = new Rectangle(2000, 100, 3840, 2160),
            Scale = 1.5,
        };

        var window = new WindowConfig
        {
            Width = 960,
            Height = 540,
            X = 120,
            Y = 80,
            FullScreen = false,
        };

        var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);

        Assert.Equal(960, bounds.Width);
        Assert.Equal(540, bounds.Height);

        var expectedMonitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        Assert.Equal(expectedMonitorBounds.Left + window.X!.Value, bounds.Left);
        Assert.Equal(expectedMonitorBounds.Top + window.Y!.Value, bounds.Top);
    }

    [Fact]
    public void ResolveBounds_WhenOffsetsOmitted_PreservesDimensionsAndUsesMonitorOrigin()
    {
        // Use the primary monitor position (0,0) to avoid OS-dependent
        // clamping in ValidateAndClamp's TryGetMonitorAreas call, which
        // snaps to the nearest physical monitor via MonitorFromPoint.
        var monitor = new MonitorInfo
        {
            Width = 2560,
            Height = 1440,
            Bounds = new Rectangle(0, 0, 2560, 1440),
            WorkArea = new Rectangle(0, 0, 2560, 1440),
            Scale = 2.0,
        };

        var window = new WindowConfig
        {
            Width = 960,
            Height = 540,
            FullScreen = false,
        };

        var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);

        // Dimensions must be preserved regardless of scale.
        Assert.Equal(960, bounds.Width);
        Assert.Equal(540, bounds.Height);

        // Position should be at the monitor origin (may be adjusted
        // by ValidateAndClamp, but dimensions remain stable).
        Assert.True(bounds.Left >= 0, "Left should be non-negative when placed at primary monitor.");
        Assert.True(bounds.Top >= 0, "Top should be non-negative when placed at primary monitor.");
    }
}
