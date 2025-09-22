using System;
using System.Collections.Concurrent;
using System.Security;
using System.Threading;

namespace Mieruka.Core.Security;

/// <summary>
/// Provides unified access to the credential vault and cookie store while caching secrets in memory.
/// </summary>
public sealed class SecretsProvider
{
    private readonly CredentialVault _vault;
    private readonly CookieSafeStore _cookieStore;
    private readonly ConcurrentDictionary<string, WeakReference<SecureString>> _secretCache = new(StringComparer.OrdinalIgnoreCase);
    private long _hits;
    private long _misses;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsProvider"/> class.
    /// </summary>
    public SecretsProvider(CredentialVault vault, CookieSafeStore cookieStore)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _cookieStore = cookieStore ?? throw new ArgumentNullException(nameof(cookieStore));
    }

    /// <summary>
    /// Saves the provided secret in the underlying vault and updates the cache.
    /// </summary>
    public void SaveSecret(string key, SecureString secret, int version = CredentialVault.CurrentSecretVersion)
    {
        _vault.SaveSecret(key, secret, version);
        _secretCache[key] = new WeakReference<SecureString>(secret.Copy());
    }

    /// <summary>
    /// Retrieves a secret, leveraging the in-memory cache when possible.
    /// </summary>
    public SecureString GetSecret(string key)
    {
        if (_secretCache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var cached))
        {
            Interlocked.Increment(ref _hits);
            return cached.Copy();
        }

        Interlocked.Increment(ref _misses);
        var secret = _vault.GetSecret(key);
        _secretCache[key] = new WeakReference<SecureString>(secret.Copy());
        return secret;
    }

    /// <summary>
    /// Deletes the specified secret.
    /// </summary>
    public void DeleteSecret(string key)
    {
        _vault.DeleteSecret(key);
        _secretCache.TryRemove(key, out _);
    }

    /// <summary>
    /// Gets the associated cookie store.
    /// </summary>
    public CookieSafeStore Cookies => _cookieStore;

    /// <summary>
    /// Returns the cache statistics.
    /// </summary>
    public (long hits, long misses) GetCacheStatistics()
    {
        return (Interlocked.Read(ref _hits), Interlocked.Read(ref _misses));
    }

    /// <summary>
    /// Clears the in-memory cache.
    /// </summary>
    public void ClearCache()
    {
        _secretCache.Clear();
    }
}
