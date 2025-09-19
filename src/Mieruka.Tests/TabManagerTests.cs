using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Automation.Tabs;
using Mieruka.Tests.TestDoubles;
using Xunit;

namespace Mieruka.Tests;

public sealed class TabManagerTests
{
    [Fact]
    public async Task MonitorAsync_RemovesBlockedWindows()
    {
        var telemetry = new FakeTelemetry();
        using var driver = new FakeWebDriver(
            new FakeWindow("allowed", "https://contoso.example/dashboard"),
            new FakeWindow("blocked", "https://ads.example/banner"));

        var manager = new TabManager(telemetry);
        using var cts = new CancellationTokenSource();
        var monitorTask = manager.MonitorAsync(driver, new[] { "contoso.example" }, cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(4.2));
        cts.Cancel();

        await monitorTask;

        Assert.DoesNotContain("blocked", driver.WindowHandles);
        Assert.Contains(telemetry.InfoMessages, message => message.Contains("Closed browser tab", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MonitorAsync_PreservesWhitelistedWindows()
    {
        var telemetry = new FakeTelemetry();
        using var driver = new FakeWebDriver(
            new FakeWindow("allowed", "https://contoso.example/dashboard"));

        var manager = new TabManager(telemetry);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await manager.MonitorAsync(driver, new[] { "contoso.example" }, cts.Token);

        Assert.Contains("allowed", driver.WindowHandles);
        Assert.DoesNotContain(telemetry.InfoMessages, message => message.Contains("Closed browser tab", StringComparison.OrdinalIgnoreCase));
    }
}
