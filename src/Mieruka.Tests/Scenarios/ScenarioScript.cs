using System;
using System.Collections.Generic;
using Mieruka.Core.Services;

namespace Mieruka.Tests.Scenarios;

/// <summary>
/// Represents a scripted scenario used to stress test performance metrics.
/// </summary>
internal sealed class ScenarioScript
{
    public ScenarioScript(
        string name,
        IReadOnlyList<FrameSample> frames,
        IReadOnlyList<DriftSample> drift,
        IReadOnlyList<CycleSample> cycles)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        Drift = drift ?? throw new ArgumentNullException(nameof(drift));
        Cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));
    }

    /// <summary>
    /// Gets the scenario name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the frame samples associated with the scenario.
    /// </summary>
    public IReadOnlyList<FrameSample> Frames { get; }

    /// <summary>
    /// Gets the drift samples associated with the scenario.
    /// </summary>
    public IReadOnlyList<DriftSample> Drift { get; }

    /// <summary>
    /// Gets the cycle samples associated with the scenario.
    /// </summary>
    public IReadOnlyList<CycleSample> Cycles { get; }

    /// <summary>
    /// Executes the scenario using the supplied metrics service.
    /// </summary>
    /// <param name="metricsService">Service responsible for generating the report.</param>
    /// <returns>The generated performance report.</returns>
    public PerformanceReport Run(PerformanceMetricsService metricsService)
    {
        if (metricsService is null)
        {
            throw new ArgumentNullException(nameof(metricsService));
        }

        return metricsService.AnalyzeAndReport(Frames, Drift, Cycles);
    }
}
