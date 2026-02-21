using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace Mieruka.Core.Security;

/// <summary>
/// Provides helper methods that sanitize user controlled input before it is used in security sensitive contexts.
/// </summary>
public static partial class InputSanitizer
{
    [GeneratedRegex(@"^[A-Za-z0-9_#:\[\].>+*\-\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SelectorRegex();

    [GeneratedRegex(@"^[A-Za-z0-9.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostRegex();

    /// <summary>
    /// Normalizes a file system path ensuring that it does not escape the provided base directory.
    /// </summary>
    /// <param name="path">Input path.</param>
    /// <param name="baseDirectory">Base directory.</param>
    /// <returns>Sanitized absolute path.</returns>
    public static string SanitizePath(string path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        path = path.Trim();
        EnsureSafeAscii(path, 260, nameof(path));

        var root = string.IsNullOrWhiteSpace(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory;
        var combined = Path.Combine(root, path);
        var normalized = Path.GetFullPath(combined);
        var rootNormalized = Path.GetFullPath(root);

        if (!normalized.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Rejected path traversal attempt while sanitizing path (argument={Argument}).", nameof(path));
            throw new InvalidOperationException("Path traversal detected.");
        }

        return normalized;
    }

    /// <summary>
    /// Sanitizes a host name, converting it to punycode when necessary.
    /// </summary>
    /// <param name="host">Host name.</param>
    /// <returns>Normalized host.</returns>
    public static string SanitizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        host = host.Trim().TrimEnd('.');
        if (host.Length > 255)
        {
            Log.Warning("Rejected host due to excessive length (length={Length}).", host.Length);
            throw new ArgumentException("Host is too long.", nameof(host));
        }

        var idn = new IdnMapping();
        var ascii = idn.GetAscii(host);
        if (!HostRegex().IsMatch(ascii))
        {
            Log.Warning("Rejected host due to invalid characters (length={Length}).", host.Length);
            throw new ArgumentException("Host contains invalid characters.", nameof(host));
        }

        return ascii.ToLowerInvariant();
    }

    /// <summary>
    /// Sanitizes a CSS selector or automation script snippet.
    /// </summary>
    /// <param name="value">Input value.</param>
    /// <param name="maxLength">Maximum allowed length.</param>
    /// <returns>Sanitized selector.</returns>
    public static string SanitizeSelector(string value, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            Log.Warning("Rejected selector due to excessive length (length={Length}, max={Max}).", value.Length, maxLength);
            throw new ArgumentException("Selector is too long.", nameof(value));
        }

        if (!SelectorRegex().IsMatch(value))
        {
            Log.Warning("Rejected selector due to invalid characters (length={Length}).", value.Length);
            throw new ArgumentException("Selector contains invalid characters.", nameof(value));
        }

        return value;
    }

    /// <summary>
    /// Validates that a string conforms to an expected ASCII range and length.
    /// </summary>
    public static void EnsureSafeAscii(string value, int maxLength, string argumentName)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maxLength)
        {
            Log.Warning("Rejected value due to excessive length (argument={Argument}, length={Length}, max={Max}).", argumentName, value.Length, maxLength);
            throw new ArgumentException($"Value exceeds maximum length of {maxLength} characters.", argumentName);
        }

        foreach (var ch in value)
        {
            if (ch < 0x20 || ch > 0x7E)
            {
                Log.Warning("Rejected value due to unsupported character (argument={Argument}).", argumentName);
                throw new ArgumentException("Value contains unsupported characters.", argumentName);
            }
        }
    }
}
