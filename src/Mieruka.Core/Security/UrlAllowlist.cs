using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mieruka.Core.Security;

/// <summary>
/// Represents a normalized allowlist of URLs that are allowed to be opened.
/// </summary>
public sealed class UrlAllowlist
{
    private readonly HashSet<string> _globalEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _siteEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly AuditLog? _auditLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlAllowlist"/> class.
    /// </summary>
    /// <param name="auditLog">Optional audit log.</param>
    public UrlAllowlist(AuditLog? auditLog = null)
    {
        _auditLog = auditLog;
    }

    /// <summary>
    /// Adds a global entry to the allowlist.
    /// </summary>
    public void Add(string host)
    {
        var normalized = NormalizeHost(host);
        if (!string.IsNullOrEmpty(normalized))
        {
            lock (_globalEntries)
            {
                _globalEntries.Add(normalized);
            }
        }
    }

    /// <summary>
    /// Removes a global entry from the allowlist.
    /// </summary>
    public void Remove(string host)
    {
        var normalized = NormalizeHost(host);
        if (!string.IsNullOrEmpty(normalized))
        {
            lock (_globalEntries)
            {
                _globalEntries.Remove(normalized);
            }
        }
    }

    /// <summary>
    /// Adds an entry scoped to the specified site identifier.
    /// </summary>
    public void Add(string siteId, string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        var normalized = NormalizeHost(host);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        var set = _siteEntries.GetOrAdd(siteId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (set)
        {
            set.Add(normalized);
        }
    }

    /// <summary>
    /// Removes an entry associated with the provided site identifier.
    /// </summary>
    public void Remove(string siteId, string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        var normalized = NormalizeHost(host);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        if (_siteEntries.TryGetValue(siteId, out var set))
        {
            lock (set)
            {
                set.Remove(normalized);
                if (set.Count == 0)
                {
                    _siteEntries.TryRemove(siteId, out _);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the supplied URL is allowed, optionally taking into account per-site overrides.
    /// </summary>
    /// <param name="uri">Target URI.</param>
    /// <param name="siteId">Optional site identifier.</param>
    /// <returns><c>true</c> when the URI is allowed.</returns>
    public bool IsAllowed(Uri uri, string? siteId = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        var key = NormalizeUri(uri);

        lock (_globalEntries)
        {
            if (ContainsMatch(_globalEntries, key))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(siteId) && _siteEntries.TryGetValue(siteId, out var set))
        {
            lock (set)
            {
                if (ContainsMatch(set, key))
                {
                    return true;
                }
            }
        }

        _auditLog?.RecordAllowlistBlock(uri.ToString(), siteId);
        return false;
    }

    /// <summary>
    /// Retrieves a snapshot of the global allowlist entries.
    /// </summary>
    public IReadOnlyCollection<string> GetGlobalEntries()
    {
        lock (_globalEntries)
        {
            return _globalEntries.ToArray();
        }
    }

    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        host = host.Trim();
        if (host.StartsWith("*.", StringComparison.Ordinal))
        {
            var tail = host[2..];
            var normalizedTail = InputSanitizer.SanitizeHost(tail);
            return string.IsNullOrEmpty(normalizedTail) ? string.Empty : "*." + normalizedTail;
        }

        return InputSanitizer.SanitizeHost(host);
    }

    private static string NormalizeUri(Uri uri)
    {
        var host = InputSanitizer.SanitizeHost(uri.Host);
        if (string.IsNullOrEmpty(host))
        {
            return string.Empty;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var port = uri.IsDefaultPort ? (scheme == "https" ? 443 : scheme == "http" ? 80 : uri.Port) : uri.Port;
        return $"{scheme}:{host}:{port}";
    }

    private static bool ContainsMatch(IEnumerable<string> entries, string key)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.StartsWith("*.", StringComparison.Ordinal))
            {
                if (MatchesWildcard(entry, key))
                {
                    return true;
                }
            }
            else if (MatchesHost(entry, key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesWildcard(string entry, string key)
    {
        var normalizedEntry = entry[2..];
        var parts = key.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        var host = parts[1];
        return host.EndsWith("." + normalizedEntry, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesHost(string entry, string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        var scheme = parts[0];
        var host = parts[1];
        var port = parts[2];

        if (!entry.Contains(':'))
        {
            return string.Equals(host, entry, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry, $"{scheme}:{host}:{port}", StringComparison.OrdinalIgnoreCase);
    }
}
