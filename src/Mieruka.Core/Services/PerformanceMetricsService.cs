using System;
using System.Collections.Generic;
using System.Linq;

namespace Mieruka.Core.Services;

/// <summary>
/// Provides utilities to analyze runtime performance metrics and report them through telemetry.
/// </summary>
public sealed class PerformanceMetricsService
{
    private readonly ITelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerformanceMetricsService"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to report the calculated metrics.</param>
    public PerformanceMetricsService(ITelemetry telemetry)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>
    /// Computes a performance report for the supplied samples and publishes it through telemetry.
    /// </summary>
    /// <param name="frames">Collection of rendered frame samples.</param>
    /// <param name="inputDrift">Collection of drift measurements that compare expected and actual timings.</param>
    /// <param name="cycleDrift">Collection of cycle duration measurements.</param>
    /// <returns>The calculated performance report.</returns>
    public PerformanceReport AnalyzeAndReport(
        IEnumerable<FrameSample> frames,
        IEnumerable<DriftSample> inputDrift,
        IEnumerable<CycleSample> cycleDrift)
    {
        var report = Analyze(frames, inputDrift, cycleDrift);
        Report(report);
        return report;
    }

    /// <summary>
    /// Computes a performance report for the supplied samples.
    /// </summary>
    /// <param name="frames">Collection of rendered frame samples.</param>
    /// <param name="inputDrift">Collection of drift measurements that compare expected and actual timings.</param>
    /// <param name="cycleDrift">Collection of cycle duration measurements.</param>
    /// <returns>The calculated performance report.</returns>
    public PerformanceReport Analyze(
        IEnumerable<FrameSample> frames,
        IEnumerable<DriftSample> inputDrift,
        IEnumerable<CycleSample> cycleDrift)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(inputDrift);
        ArgumentNullException.ThrowIfNull(cycleDrift);

        var frameSamples = frames.ToArray();
        var frameDurations = frameSamples
            .Select(sample => sample.Duration.TotalMilliseconds)
            .Where(duration => duration > 0 && !double.IsNaN(duration) && !double.IsInfinity(duration))
            .ToArray();
        var driftSamples = inputDrift.ToArray();
        var cycleSamples = cycleDrift.ToArray();

        var averageFrameDuration = frameDurations.Length == 0
            ? 0d
            : frameDurations.Average();

        var medianFrameDuration = frameDurations.Length == 0
            ? 0d
            : Median(frameDurations);

        var averageFps = averageFrameDuration <= 0 ? 0 : 1000d / averageFrameDuration;
        var medianFps = medianFrameDuration <= 0 ? 0 : 1000d / medianFrameDuration;

        var driftMetrics = CalculateDriftStatistics(driftSamples.Select(sample => sample.DriftMilliseconds));
        var cycleMetrics = CalculateDriftStatistics(cycleSamples.Select(sample => sample.DriftMilliseconds));

        return new PerformanceReport(
            averageFps,
            medianFps,
            driftMetrics,
            cycleMetrics,
            frameSamples.Length);
    }

    /// <summary>
    /// Publishes the supplied performance report to the configured telemetry sink.
    /// </summary>
    /// <param name="report">Performance report to publish.</param>
    public void Report(PerformanceReport report)
    {
        var message = FormattableString.Invariant(
            $"Relatório de performance — FPS médio: {report.AverageFps:F2} (mediana {report.MedianFps:F2}), drift médio: {report.InputDrift.AverageMilliseconds:F2} ms (pico {report.InputDrift.PeakMilliseconds:F2} ms), drift ciclo: {report.CycleTiming.AverageMilliseconds:F2} ms (pico {report.CycleTiming.PeakMilliseconds:F2} ms), quadros analisados: {report.FrameCount}.");

        _telemetry.Info(message);
    }

    private static DriftStatistics CalculateDriftStatistics(IEnumerable<double> samples)
    {
        var values = samples
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .Select(Math.Abs)
            .ToArray();

        if (values.Length == 0)
        {
            return DriftStatistics.Empty;
        }

        return new DriftStatistics(values.Average(), values.Max());
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();

        if (ordered.Length == 0)
        {
            return 0d;
        }

        var middle = ordered.Length / 2;
        if (ordered.Length % 2 == 0)
        {
            return (ordered[middle - 1] + ordered[middle]) / 2d;
        }

        return ordered[middle];
    }
}

/// <summary>
/// Represents a single rendered frame.
/// </summary>
/// <param name="Duration">Duration of the frame.</param>
public sealed record class FrameSample(TimeSpan Duration)
{
    /// <summary>
    /// Creates a new <see cref="FrameSample"/> from a time span expressed in milliseconds.
    /// </summary>
    /// <param name="milliseconds">Frame duration in milliseconds.</param>
    /// <returns>The created sample.</returns>
    public static FrameSample FromMilliseconds(double milliseconds)
        => new(TimeSpan.FromMilliseconds(milliseconds));
}

/// <summary>
/// Represents a drift measurement between an expected timestamp and the observed one.
/// </summary>
/// <param name="Expected">Expected timestamp.</param>
/// <param name="Actual">Observed timestamp.</param>
public sealed record class DriftSample(DateTimeOffset Expected, DateTimeOffset Actual)
{
    /// <summary>
    /// Gets the drift between expected and observed timestamps in milliseconds.
    /// </summary>
    public double DriftMilliseconds => (Actual - Expected).TotalMilliseconds;
}

/// <summary>
/// Represents the drift for a playback cycle item.
/// </summary>
/// <param name="ItemId">Identifier of the cycle item.</param>
/// <param name="ExpectedDuration">Configured duration of the item.</param>
/// <param name="ActualDuration">Observed duration of the item.</param>
public sealed record class CycleSample(string ItemId, TimeSpan ExpectedDuration, TimeSpan ActualDuration)
{
    /// <summary>
    /// Gets the drift between expected and observed durations in milliseconds.
    /// </summary>
    public double DriftMilliseconds => (ActualDuration - ExpectedDuration).TotalMilliseconds;
}

/// <summary>
/// Aggregated statistics for a set of drift measurements.
/// </summary>
/// <param name="AverageMilliseconds">Average drift in milliseconds.</param>
/// <param name="PeakMilliseconds">Maximum drift in milliseconds.</param>
public sealed record class DriftStatistics(double AverageMilliseconds, double PeakMilliseconds)
{
    /// <summary>
    /// Gets an empty instance representing the absence of data.
    /// </summary>
    public static DriftStatistics Empty { get; } = new(0d, 0d);
}

/// <summary>
/// Represents the outcome of a performance analysis.
/// </summary>
/// <param name="AverageFps">Average frames per second.</param>
/// <param name="MedianFps">Median frames per second.</param>
/// <param name="InputDrift">Aggregate drift for input events.</param>
/// <param name="CycleTiming">Aggregate drift for cycle durations.</param>
/// <param name="FrameCount">Number of frames considered in the report.</param>
public sealed record class PerformanceReport(
    double AverageFps,
    double MedianFps,
    DriftStatistics InputDrift,
    DriftStatistics CycleTiming,
    int FrameCount);
