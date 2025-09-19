using System;
using System.Collections.Generic;
using Mieruka.Core.Services;
using Mieruka.Tests.Scenarios;
using Mieruka.Tests.TestDoubles;
using Xunit;

namespace Mieruka.Tests;

public sealed class ScenarioScriptsTests
{
    public static IEnumerable<object[]> ScenarioData()
    {
        var origin = DateTimeOffset.UtcNow;
        yield return new object[] { ScenarioScriptFactory.CreateHotPlugScenario(origin) };
        yield return new object[] { ScenarioScriptFactory.CreateRemoteDesktopScenario(origin.AddMinutes(1)) };
        yield return new object[] { ScenarioScriptFactory.CreateDpiScalingScenario(origin.AddMinutes(2), 1.0, 1.25) };
    }

    [Theory]
    [MemberData(nameof(ScenarioData))]
    public void ScenarioScript_Run_GeneratesReport(ScenarioScript script)
    {
        var telemetry = new FakeTelemetry();
        var service = new PerformanceMetricsService(telemetry);

        var report = script.Run(service);

        Assert.NotNull(report);
        Assert.NotEqual(0, report.FrameCount);
        Assert.True(report.AverageFps > 0);
        Assert.True(report.MedianFps > 0);
        Assert.True(report.CycleTiming.PeakMilliseconds >= report.CycleTiming.AverageMilliseconds);

        var message = Assert.Single(telemetry.InfoMessages);
        Assert.Contains("Relatório de performance", message);
        Assert.Contains("drift", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FPS", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScenarioScripts_ProduceDistinctProfiles()
    {
        var origin = DateTimeOffset.UtcNow;
        var telemetry = new FakeTelemetry();
        var service = new PerformanceMetricsService(telemetry);

        var hotPlug = ScenarioScriptFactory.CreateHotPlugScenario(origin);
        var rdp = ScenarioScriptFactory.CreateRemoteDesktopScenario(origin);
        var dpi = ScenarioScriptFactory.CreateDpiScalingScenario(origin, 1.0, 1.5);

        var hotPlugReport = hotPlug.Run(service);
        var rdpReport = rdp.Run(service);
        var dpiReport = dpi.Run(service);

        Assert.True(hotPlugReport.CycleTiming.PeakMilliseconds > dpiReport.CycleTiming.PeakMilliseconds);
        Assert.True(rdpReport.AverageFps < hotPlugReport.AverageFps);
        Assert.True(dpiReport.InputDrift.AverageMilliseconds < rdpReport.InputDrift.AverageMilliseconds);
    }
}
