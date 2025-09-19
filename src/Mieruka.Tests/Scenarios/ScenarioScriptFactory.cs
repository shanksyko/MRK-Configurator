using System;
using System.Collections.Generic;
using Mieruka.Core.Services;

namespace Mieruka.Tests.Scenarios;

internal static class ScenarioScriptFactory
{
    public static ScenarioScript CreateHotPlugScenario(DateTimeOffset origin)
    {
        var frames = new List<FrameSample>
        {
            FrameSample.FromMilliseconds(16.6),
            FrameSample.FromMilliseconds(17.2),
            FrameSample.FromMilliseconds(21.4),
            FrameSample.FromMilliseconds(18.3),
            FrameSample.FromMilliseconds(19.9),
        };

        var drift = new List<DriftSample>
        {
            new(origin, origin),
            new(origin.AddSeconds(1), origin.AddSeconds(1).AddMilliseconds(12)),
            new(origin.AddSeconds(2), origin.AddSeconds(2).AddMilliseconds(26)),
            new(origin.AddSeconds(3), origin.AddSeconds(3).AddMilliseconds(38)),
            new(origin.AddSeconds(4), origin.AddSeconds(4).AddMilliseconds(44)),
        };

        var cycles = new List<CycleSample>
        {
            new("cycle-app", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(92.4)),
            new("cycle-site", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(125.2)),
        };

        return new ScenarioScript("hot-plug", frames, drift, cycles);
    }

    public static ScenarioScript CreateRemoteDesktopScenario(DateTimeOffset origin)
    {
        var frames = new List<FrameSample>
        {
            FrameSample.FromMilliseconds(16.9),
            FrameSample.FromMilliseconds(23.1),
            FrameSample.FromMilliseconds(25.4),
            FrameSample.FromMilliseconds(24.9),
            FrameSample.FromMilliseconds(22.8),
        };

        var drift = new List<DriftSample>
        {
            new(origin, origin.AddMilliseconds(8)),
            new(origin.AddSeconds(1), origin.AddSeconds(1).AddMilliseconds(34)),
            new(origin.AddSeconds(2), origin.AddSeconds(2).AddMilliseconds(47)),
            new(origin.AddSeconds(3), origin.AddSeconds(3).AddMilliseconds(52)),
            new(origin.AddSeconds(4), origin.AddSeconds(4).AddMilliseconds(63)),
        };

        var cycles = new List<CycleSample>
        {
            new("cycle-app", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(97.5)),
            new("cycle-site", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(131.2)),
        };

        return new ScenarioScript("rdp", frames, drift, cycles);
    }

    public static ScenarioScript CreateDpiScalingScenario(DateTimeOffset origin, double initialScale, double finalScale)
    {
        if (initialScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialScale));
        }

        if (finalScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalScale));
        }

        var frames = new List<FrameSample>
        {
            FrameSample.FromMilliseconds(16.0),
            FrameSample.FromMilliseconds(16.2),
            FrameSample.FromMilliseconds(17.1),
            FrameSample.FromMilliseconds(18.6),
            FrameSample.FromMilliseconds(19.0),
        };

        var drift = new List<DriftSample>
        {
            new(origin, origin),
            new(origin.AddSeconds(1), origin.AddSeconds(1).AddMilliseconds(6)),
            new(origin.AddSeconds(2), origin.AddSeconds(2).AddMilliseconds(9)),
            new(origin.AddSeconds(3), origin.AddSeconds(3).AddMilliseconds(14)),
        };

        var baseWidth = 1920d;

        var initialDipWidth = DisplayUtils.PxToDip(baseWidth, initialScale);
        var finalDipWidth = DisplayUtils.PxToDip(baseWidth, finalScale);

        var deltaWidth = Math.Abs(finalDipWidth - initialDipWidth);
        var adjustmentMs = deltaWidth * 0.02;

        var cycles = new List<CycleSample>
        {
            new("cycle-app", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90).Add(TimeSpan.FromMilliseconds(adjustmentMs))),
            new("cycle-site", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120).Add(TimeSpan.FromMilliseconds(adjustmentMs * 1.1))),
        };

        return new ScenarioScript("dpi", frames, drift, cycles);
    }
}
