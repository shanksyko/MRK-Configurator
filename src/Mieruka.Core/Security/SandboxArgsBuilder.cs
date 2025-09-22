using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mieruka.Core.Security;

/// <summary>
/// Builds hardened browser command line arguments.
/// </summary>
public sealed class SandboxArgsBuilder
{
    private readonly List<string> _arguments = new();
    private readonly HashSet<string> _applied = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxArgsBuilder"/> class.
    /// </summary>
    /// <param name="profileId">Profile identifier used to isolate user data.</param>
    /// <param name="baseDirectory">Base directory for user data storage.</param>
    public SandboxArgsBuilder(string profileId, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        _arguments.Add("--no-first-run");
        _arguments.Add("--disable-sync");
        _arguments.Add("--disable-extensions");
        _arguments.Add("--disable-features=Translate");

        var root = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mieruka", "profiles");
        Directory.CreateDirectory(root);
        var userData = Path.Combine(root, EnsureSafeProfileId(profileId));
        _arguments.Add(Format("--user-data-dir", userData));
    }

    /// <summary>
    /// Enables kiosk mode.
    /// </summary>
    public SandboxArgsBuilder UseKioskMode()
    {
        return Add("--kiosk");
    }

    /// <summary>
    /// Enables app mode for the provided URL.
    /// </summary>
    public SandboxArgsBuilder UseAppMode(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("URL must be absolute.", nameof(url));
        }

        return Add(Format("--app", SanitizeUrl(url)));
    }

    /// <summary>
    /// Enables incognito mode.
    /// </summary>
    public SandboxArgsBuilder UseIncognito()
    {
        return Add("--incognito");
    }

    /// <summary>
    /// Configures the builder to use an automation proxy.
    /// </summary>
    public SandboxArgsBuilder UseProxy(string proxyAddress, IEnumerable<string>? bypassList = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(proxyAddress);
        var sanitized = SanitizeProxy(proxyAddress);
        Add(Format("--proxy-server", sanitized));

        if (bypassList is not null)
        {
            var entries = bypassList
                .Select(entry => InputSanitizer.SanitizeHost(entry))
                .Where(entry => !string.IsNullOrEmpty(entry));
            Add(Format("--proxy-bypass-list", string.Join(";", entries)));
        }

        return this;
    }

    /// <summary>
    /// Adds an arbitrary argument after sanitization.
    /// </summary>
    public SandboxArgsBuilder Add(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return this;
        }

        if (_applied.Add(argument))
        {
            _arguments.Add(argument);
        }

        return this;
    }

    /// <summary>
    /// Returns the final list of arguments.
    /// </summary>
    public IReadOnlyList<string> Build()
    {
        return _arguments.AsReadOnly();
    }

    private static string Format(string name, string value)
    {
        var sanitizedValue = value.Replace("\"", string.Empty, StringComparison.Ordinal);
        return $"{name}=\"{sanitizedValue}\"";
    }

    private static string SanitizeUrl(Uri uri)
    {
        var builder = new StringBuilder();
        builder.Append(uri.Scheme.ToLowerInvariant());
        builder.Append("://");
        builder.Append(InputSanitizer.SanitizeHost(uri.Host));
        if (!uri.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        var path = uri.GetComponents(UriComponents.Path | UriComponents.Query | UriComponents.Fragment, UriFormat.UriEscaped);
        builder.Append(path);
        return builder.ToString();
    }

    private static string SanitizeProxy(string value)
    {
        if (!value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Proxy must include a scheme and host.", nameof(value));
        }

        return value.Trim();
    }
    private static string EnsureSafeProfileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Profile id must be provided.", nameof(value));
        }

        var sanitized = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        if (sanitized.Length == 0)
        {
            throw new ArgumentException("Profile id is not valid.", nameof(value));
        }

        return sanitized.ToLowerInvariant();
    }
}
