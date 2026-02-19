using System;
using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Configuration for scheduled orchestrator start/stop based on time of day and day of week.
/// </summary>
public sealed record ScheduleConfig
{
    /// <summary>Whether scheduling is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Time of day to start the orchestrator (local time).</summary>
    public TimeOnly? StartTime { get; init; }

    /// <summary>Time of day to stop the orchestrator (local time).</summary>
    public TimeOnly? StopTime { get; init; }

    /// <summary>Days of the week on which the schedule is active. Empty means all days.</summary>
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; } = Array.Empty<DayOfWeek>();
}
