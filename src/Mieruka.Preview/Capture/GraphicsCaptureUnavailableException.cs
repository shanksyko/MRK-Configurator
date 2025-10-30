using System;

namespace Mieruka.Preview;

/// <summary>
/// Represents an error indicating that Windows Graphics Capture cannot be used on the current host.
/// </summary>
public sealed class GraphicsCaptureUnavailableException : NotSupportedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsCaptureUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the failure.</param>
    /// <param name="isPermanent">Indicates whether the failure should be treated as permanent for the current session.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public GraphicsCaptureUnavailableException(string message, bool isPermanent, Exception? innerException = null)
        : base(message, innerException)
    {
        IsPermanent = isPermanent;
    }

    /// <summary>
    /// Gets a value indicating whether the failure should be treated as permanent for the current session.
    /// </summary>
    public bool IsPermanent { get; }
}
