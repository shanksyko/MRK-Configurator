using System;
using System.Linq;
using Mieruka.Core.Services;
using Mieruka.Tests.TestDoubles;
using Xunit;

namespace Mieruka.Tests;

public sealed class PerformanceMetricsServiceTests
{
    [Fact]
    public void AnalyzeAndReport_ComputesStatisticsAndLogs()
    {
        var telemetry = new FakeTelemetry();
        var service = new PerformanceMetricsService(telemetry);
        var origin = DateTimeOffset.UtcNow;

        var frames = new[]
        {
            FrameSample.FromMilliseconds(16.0),
            FrameSample.FromMilliseconds(17.2),
            FrameSample.FromMilliseconds(15.6),
            FrameSample.FromMilliseconds(16.8),
        };

        var drift = new[]
        {
            new DriftSample(origin, origin),
            new DriftSample(origin.AddSeconds(1), origin.AddSeconds(1).AddMilliseconds(8)),
            new DriftSample(origin.AddSeconds(2), origin.AddSeconds(2).AddMilliseconds(12)),
            new DriftSample(origin.AddSeconds(3), origin.AddSeconds(3).AddMilliseconds(15)),
        };

        var cycles = new[]
        {
            new CycleSample("cycle-app", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(92.2)),
            new CycleSample("cycle-site", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(123.8)),
        };

        var report = service.AnalyzeAndReport(frames, drift, cycles);

        Assert.Equal(4, report.FrameCount);
        Assert.InRange(report.AverageFps, 59.0, 65.0);
        Assert.InRange(report.MedianFps, 59.0, 65.0);
        Assert.InRange(report.InputDrift.AverageMilliseconds, 8.0, 12.0);
        Assert.InRange(report.InputDrift.PeakMilliseconds, 14.0, 16.0);
        Assert.InRange(report.CycleTiming.AverageMilliseconds, 1800, 2300);
        Assert.True(report.CycleTiming.PeakMilliseconds >= report.CycleTiming.AverageMilliseconds);

        var message = Assert.Single(telemetry.InfoMessages);
        Assert.Contains("Relatório de performance", message);
        Assert.Contains("FPS médio", message);
        Assert.Contains("drift médio", message);
    }

    [Fact]
    public void Analyze_WhenNoSamples_ReturnsEmptyReport()
    {
        var telemetry = new FakeTelemetry();
        var service = new PerformanceMetricsService(telemetry);

        var report = service.Analyze(Array.Empty<FrameSample>(), Array.Empty<DriftSample>(), Array.Empty<CycleSample>());

        Assert.Equal(0, report.FrameCount);
        Assert.Equal(0, report.AverageFps);
        Assert.Equal(0, report.MedianFps);
        Assert.Equal(0, report.InputDrift.AverageMilliseconds);
        Assert.Equal(0, report.InputDrift.PeakMilliseconds);
        Assert.Equal(0, report.CycleTiming.AverageMilliseconds);
        Assert.Equal(0, report.CycleTiming.PeakMilliseconds);
        Assert.Empty(telemetry.InfoMessages);
    }

    [Fact]
    public void Analyze_FiltersInvalidSamples()
    {
        var telemetry = new FakeTelemetry();
        var service = new PerformanceMetricsService(telemetry);
        var origin = DateTimeOffset.UtcNow;

        var frames = new[]
        {
            new FrameSample(TimeSpan.Zero),
            FrameSample.FromMilliseconds(16.7),
            FrameSample.FromMilliseconds(16.9),
        };

        var drift = new[]
        {
            new DriftSample(origin, origin.AddMilliseconds(-4)),
            new DriftSample(origin.AddSeconds(1), origin.AddSeconds(1).AddMilliseconds(10)),
        };

        var cycles = new[]
        {
            new CycleSample("item", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(59.5)),
            new CycleSample("item", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60.5)),
        };

        var report = service.Analyze(frames, drift, cycles);

        Assert.Equal(3, report.FrameCount);
        Assert.InRange(report.AverageFps, 55.0, 65.0);
        Assert.InRange(report.MedianFps, 55.0, 65.0);
        Assert.InRange(report.InputDrift.AverageMilliseconds, 6.0, 8.0);
        Assert.Equal(500, report.CycleTiming.PeakMilliseconds, 0);
    }
}
