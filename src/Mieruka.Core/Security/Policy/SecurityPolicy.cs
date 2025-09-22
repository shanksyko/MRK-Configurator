using System;
using System.Collections.Concurrent;

namespace Mieruka.Core.Security.Policy;

/// <summary>
/// Defines the security posture applied to the player.
/// </summary>
public sealed class SecurityPolicy
{
    private readonly ConcurrentDictionary<string, SecurityPolicyOverrides> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityPolicy"/> class.
    /// </summary>
    /// <param name="profile">Profile applied globally.</param>
    public SecurityPolicy(SecurityProfile profile = SecurityProfile.Standard)
    {
        SetProfile(profile);
    }

    /// <summary>
    /// Gets the active profile.
    /// </summary>
    public SecurityProfile Profile { get; private set; }

    /// <summary>
    /// Gets a value indicating whether cookie restoration is allowed.
    /// </summary>
    public bool AllowCookieRestore { get; private set; }

    /// <summary>
    /// Gets a value indicating whether DevTools can manipulate cookies.
    /// </summary>
    public bool AllowDevToolsCookieOperations { get; private set; }

    /// <summary>
    /// Gets a value indicating whether strict TLS validation should be enforced.
    /// </summary>
    public bool StrictTls { get; private set; }

    /// <summary>
    /// Gets a value indicating whether third-party cookies are disabled.
    /// </summary>
    public bool DisableThirdPartyCookies { get; private set; }

    /// <summary>
    /// Gets the maximum duration a login automation is allowed to run.
    /// </summary>
    public int MaxLoginDurationSeconds { get; private set; }

    /// <summary>
    /// Updates the active profile and resets the baseline values.
    /// </summary>
    public void SetProfile(SecurityProfile profile)
    {
        Profile = profile;
        (AllowCookieRestore, AllowDevToolsCookieOperations, StrictTls, DisableThirdPartyCookies, MaxLoginDurationSeconds) = profile switch
        {
            SecurityProfile.Relaxed => (true, true, false, false, 3600),
            SecurityProfile.Strict => (false, false, true, true, 900),
            _ => (true, false, true, true, 1800),
        };
    }

    /// <summary>
    /// Registers overrides for a specific site identifier.
    /// </summary>
    public void SetOverrides(string siteId, SecurityPolicyOverrides overrides)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        _overrides[siteId] = overrides;
    }

    /// <summary>
    /// Removes overrides associated with the site identifier.
    /// </summary>
    public void RemoveOverrides(string siteId)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        _overrides.TryRemove(siteId, out _);
    }

    /// <summary>
    /// Resolves the effective policy for the supplied site.
    /// </summary>
    public SecurityPolicySnapshot Resolve(string? siteId = null)
    {
        var snapshot = new SecurityPolicySnapshot(
            AllowCookieRestore,
            AllowDevToolsCookieOperations,
            StrictTls,
            DisableThirdPartyCookies,
            MaxLoginDurationSeconds);

        if (!string.IsNullOrEmpty(siteId) && _overrides.TryGetValue(siteId, out var overrides))
        {
            snapshot = snapshot with
            {
                AllowCookieRestore = overrides.AllowCookieRestore ?? snapshot.AllowCookieRestore,
                AllowDevToolsCookieOperations = overrides.AllowDevToolsCookieOperations ?? snapshot.AllowDevToolsCookieOperations,
                StrictTls = overrides.StrictTls ?? snapshot.StrictTls,
                DisableThirdPartyCookies = overrides.DisableThirdPartyCookies ?? snapshot.DisableThirdPartyCookies,
                MaxLoginDurationSeconds = overrides.MaxLoginDurationSeconds ?? snapshot.MaxLoginDurationSeconds,
            };
        }

        return snapshot;
    }

    /// <summary>
    /// Updates global settings after construction.
    /// </summary>
    public void ApplyOverrides(SecurityPolicyOverrides overrides)
    {
        AllowCookieRestore = overrides.AllowCookieRestore ?? AllowCookieRestore;
        AllowDevToolsCookieOperations = overrides.AllowDevToolsCookieOperations ?? AllowDevToolsCookieOperations;
        StrictTls = overrides.StrictTls ?? StrictTls;
        DisableThirdPartyCookies = overrides.DisableThirdPartyCookies ?? DisableThirdPartyCookies;
        MaxLoginDurationSeconds = overrides.MaxLoginDurationSeconds ?? MaxLoginDurationSeconds;
    }

    /// <summary>
    /// Validates the policy configuration.
    /// </summary>
    public void Validate()
    {
        if (MaxLoginDurationSeconds <= 0 || MaxLoginDurationSeconds > 24 * 3600)
        {
            throw new InvalidOperationException("MaxLoginDurationSeconds must be within 1 and 86400.");
        }
    }
}

/// <summary>
/// Supported security profiles.
/// </summary>
public enum SecurityProfile
{
    Relaxed,
    Standard,
    Strict,
}

/// <summary>
/// Represents optional overrides applied to a security policy.
/// </summary>
public sealed record class SecurityPolicyOverrides
{
    public bool? AllowCookieRestore { get; init; }

    public bool? AllowDevToolsCookieOperations { get; init; }

    public bool? StrictTls { get; init; }

    public bool? DisableThirdPartyCookies { get; init; }

    public int? MaxLoginDurationSeconds { get; init; }
}

/// <summary>
/// Snapshot of the effective security policy values.
/// </summary>
public sealed record class SecurityPolicySnapshot(
    bool AllowCookieRestore,
    bool AllowDevToolsCookieOperations,
    bool StrictTls,
    bool DisableThirdPartyCookies,
    int MaxLoginDurationSeconds);
