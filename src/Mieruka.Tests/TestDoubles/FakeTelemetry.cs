using System;
using System.Collections.Generic;
using Mieruka.Core.Services;

namespace Mieruka.Tests.TestDoubles;

internal sealed class FakeTelemetry : ITelemetry
{
    public List<string> InfoMessages { get; } = new();

    public List<string> WarnMessages { get; } = new();

    public List<string> ErrorMessages { get; } = new();

    public void Info(string message, Exception? exception = null)
        => InfoMessages.Add(Format(message, exception));

    public void Warn(string message, Exception? exception = null)
        => WarnMessages.Add(Format(message, exception));

    public void Error(string message, Exception? exception = null)
        => ErrorMessages.Add(Format(message, exception));

    private static string Format(string message, Exception? exception)
    {
        if (exception is null)
        {
            return message;
        }

        return $"{message} :: {exception.GetType().Name}: {exception.Message}";
    }
}
