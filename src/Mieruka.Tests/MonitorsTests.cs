using System;
using System.Collections.Generic;
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
            new MonitorDescriptor { DeviceName = "\\\\.\\DISPLAY1" }
        };

        var service = new MonitorService(
            _ => throw new InvalidOperationException("boom"),
            _ => fallback,
            () => true);

        var result = service.GetAll();

        Assert.Same(fallback, result);
    }
}
