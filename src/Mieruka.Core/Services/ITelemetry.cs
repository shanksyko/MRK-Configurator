using System;

namespace Mieruka.Core.Services;

/// <summary>
/// Abstraction used to capture telemetry events produced by the application.
/// </summary>
public interface ITelemetry
{
    /// <summary>
    /// Records informational details about the application's execution.
    /// </summary>
    /// <param name="message">Textual description of the event.</param>
    /// <param name="exception">Optional exception associated with the event.</param>
    void Info(string message, Exception? exception = null);

    /// <summary>
    /// Records potential problems that do not stop the application.
    /// </summary>
    /// <param name="message">Textual description of the warning.</param>
    /// <param name="exception">Optional exception associated with the warning.</param>
    void Warn(string message, Exception? exception = null);

    /// <summary>
    /// Records unexpected errors.
    /// </summary>
    /// <param name="message">Textual description of the error.</param>
    /// <param name="exception">Optional exception associated with the error.</param>
    void Error(string message, Exception? exception = null);
}
