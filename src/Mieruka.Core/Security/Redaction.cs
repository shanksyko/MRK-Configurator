using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Mieruka.Core.Security;

/// <summary>
/// Provides helpers that redact sensitive information from logs.
/// </summary>
public static class Redaction
{
    private static readonly Regex EmailRegex = new(
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TokenRegex = new(
        @"(?:(?:bearer|token|api[_-]?key|password)\s*[=:\s]\s*)([A-Za-z0-9._-]{6,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Redacts sensitive information from the input string.
    /// </summary>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var result = EmailRegex.Replace(value, "***@***");
        result = TokenRegex.Replace(result, match => match.Groups[1].Success ? match.Value.Replace(match.Groups[1].Value, "<redacted>") : match.Value);
        result = GuidRegex.Replace(result, "<id>");
        return result;
    }

    /// <summary>
    /// Redacts sensitive content from an HTTP log entry.
    /// </summary>
    public static HttpLogEntry Redact(HttpLogEntry entry)
    {
        var headers = entry.Headers
            .ToFrozenDictionary(
                pair => pair.Key,
                pair => Redact(pair.Value),
                StringComparer.OrdinalIgnoreCase);

        return entry with
        {
            Url = Redact(entry.Url),
            Body = Redact(entry.Body),
            Headers = headers,
        };
    }
}

/// <summary>
/// Represents a structured HTTP log entry.
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Url">Target URL.</param>
/// <param name="Headers">Headers associated with the request or response.</param>
/// <param name="Body">Body payload.</param>
public sealed record class HttpLogEntry(string Method, string Url, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    /// <summary>
    /// Gets an empty entry.
    /// </summary>
    public static HttpLogEntry Empty { get; } = new("GET", string.Empty, FrozenDictionary<string, string>.Empty, null);
}

/// <summary>
/// Serilog enricher that applies redaction to string properties.
/// </summary>
public sealed class RedactionEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Avoid allocating a new list on every log event
        var propertiesToUpdate = new List<LogEventProperty>();

        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue scalar && scalar.Value is string text)
            {
                var sanitized = Redaction.Redact(text);
                if (!ReferenceEquals(text, sanitized))
                {
                    var replacement = propertyFactory.CreateProperty(property.Key, sanitized);
                    propertiesToUpdate.Add(replacement);
                }
            }
        }

        foreach (var property in propertiesToUpdate)
        {
            logEvent.AddOrUpdateProperty(property);
        }

        if (logEvent.Exception is not null)
        {
            var sanitized = Redaction.Redact(logEvent.Exception.Message);
            if (!string.Equals(logEvent.Exception.Message, sanitized, StringComparison.Ordinal))
            {
                var property = propertyFactory.CreateProperty("exception_message", sanitized);
                logEvent.AddPropertyIfAbsent(property);
            }
        }
    }
}
