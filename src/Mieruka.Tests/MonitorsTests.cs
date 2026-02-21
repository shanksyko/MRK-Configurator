using System;
using System.Collections.Generic;
using System.Drawing;
using Mieruka.Core.Monitors;
using Xunit;

namespace Mieruka.Tests;

public sealed class MonitorsTests
{
    [Fact]
    public void GetAll_UsesGdiFallbackWhenDisplayConfigFails()
    {
        var fallback = new List<MonitorDescriptor>
        {
            new MonitorDescriptor
            {
                DeviceName = "\\\\.\\DISPLAY1",
                Width = 1920,
                Height = 1080,
                Bounds = new Rectangle(0, 0, 1920, 1080),
            }
        };

        var service = new MonitorService(
            _ => throw new InvalidOperationException("boom"),
            _ => fallback,
            () => true);

        var result = service.GetAll();

        var monitor = Assert.Single(result);
        Assert.Equal("\\\\.\\DISPLAY1", monitor.DeviceName);
        Assert.Equal(1920, monitor.Width);
        Assert.Equal(1080, monitor.Height);
    }
}
