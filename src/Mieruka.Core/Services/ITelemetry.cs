using System.Collections.Generic;

namespace Mieruka.Core.Services;

/// <summary>
/// Abstraction used to capture telemetry events produced by the application.
/// </summary>
public interface ITelemetry
{
    /// <summary>
    /// Records an event occurrence.
    /// </summary>
    /// <param name="eventName">Telemetry event name.</param>
    /// <param name="properties">Optional properties associated with the event.</param>
    void TrackEvent(string eventName, IReadOnlyDictionary<string, string>? properties = null);
}
