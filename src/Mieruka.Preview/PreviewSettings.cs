using System;

namespace Mieruka.Preview;

/// <summary>
/// Centralized preview rendering settings.
/// </summary>
public sealed class PreviewSettings
{
    public static PreviewSettings Default { get; } = new();

    public double TargetFpsGpu { get; init; } = 60d;

    public double TargetFpsGdi { get; init; } = 30d;

    public static TimeSpan CalculateFrameInterval(double framesPerSecond)
    {
        if (framesPerSecond <= 0 || double.IsNaN(framesPerSecond) || double.IsInfinity(framesPerSecond))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(1d / framesPerSecond);
    }

    public TimeSpan GetGpuFrameInterval() => CalculateFrameInterval(TargetFpsGpu);

    public TimeSpan GetGdiFrameInterval() => CalculateFrameInterval(TargetFpsGdi);
}
