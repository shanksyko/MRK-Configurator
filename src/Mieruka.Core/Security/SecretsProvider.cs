using System;
using System.Collections.Concurrent;
using System.Security;

#nullable enable

namespace Mieruka.Core.Security;

/// <summary>
/// Provides unified access to the credential vault and cookie store while caching secrets in memory.
/// </summary>
public sealed class SecretsProvider
{
    private readonly CredentialVault _vault;
    private readonly CookieSafeStore _cookieStore;
    private readonly ConcurrentDictionary<string, CacheEntry> _secretCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cacheDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsProvider"/> class.
    /// </summary>
    /// <param name="vault">Vault used to persist credentials.</param>
    /// <param name="cookieStore">Cookie store used to persist browser cookies.</param>
    /// <param name="cacheDuration">Optional duration applied to cached secrets.</param>
    public SecretsProvider(CredentialVault vault, CookieSafeStore cookieStore, TimeSpan? cacheDuration = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _cookieStore = cookieStore ?? throw new ArgumentNullException(nameof(cookieStore));
        _cacheDuration = cacheDuration is { } duration && duration > TimeSpan.Zero
            ? duration
            : TimeSpan.FromSeconds(45);
    }

    /// <summary>
    /// Persists credentials for the provided site in the underlying vault.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <param name="username">Username stored in the vault.</param>
    /// <param name="password">Password stored in the vault.</param>
    public void SaveCredentials(string siteId, SecureString username, SecureString password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        SaveSecretInternal(CredentialVault.BuildUsernameKey(siteId), username);
        SaveSecretInternal(CredentialVault.BuildPasswordKey(siteId), password);
    }

    /// <summary>
    /// Persists a TOTP secret for the provided site.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <param name="totp">TOTP seed stored in the vault.</param>
    public void SaveTotp(string siteId, SecureString totp)
    {
        ArgumentNullException.ThrowIfNull(totp);

        SaveSecretInternal(CredentialVault.BuildTotpKey(siteId), totp);
    }

    /// <summary>
    /// Retrieves the username stored for the provided site.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <returns>The stored username when present; otherwise, <c>null</c>.</returns>
    public SecureString? GetUsernameFor(string siteId)
    {
        return GetSecretInternal(CredentialVault.BuildUsernameKey(siteId));
    }

    /// <summary>
    /// Retrieves the password stored for the provided site.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <returns>The stored password when present; otherwise, <c>null</c>.</returns>
    public SecureString? GetPasswordFor(string siteId)
    {
        return GetSecretInternal(CredentialVault.BuildPasswordKey(siteId));
    }

    /// <summary>
    /// Retrieves the TOTP seed stored for the provided site.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <returns>The stored TOTP seed when present; otherwise, <c>null</c>.</returns>
    public SecureString? GetTotpFor(string siteId)
    {
        return GetSecretInternal(CredentialVault.BuildTotpKey(siteId));
    }

    /// <summary>
    /// Deletes credentials stored for the provided site.
    /// </summary>
    public void DeleteCredentials(string siteId)
    {
        DeleteSecretInternal(CredentialVault.BuildUsernameKey(siteId));
        DeleteSecretInternal(CredentialVault.BuildPasswordKey(siteId));
    }

    /// <summary>
    /// Deletes the stored TOTP seed for the provided site.
    /// </summary>
    public void DeleteTotp(string siteId)
    {
        DeleteSecretInternal(CredentialVault.BuildTotpKey(siteId));
    }

    /// <summary>
    /// Gets the associated cookie store.
    /// </summary>
    public CookieSafeStore Cookies => _cookieStore;

    /// <summary>
    /// Clears the in-memory cache.
    /// </summary>
    public void ClearCache() => _secretCache.Clear();

    /// <summary>
    /// Signals that the editor lost focus, invalidating cached secrets.
    /// </summary>
    public void NotifyFocusLost() => ClearCache();

    private SecureString? GetSecretInternal(string key)
    {
        if (_secretCache.TryGetValue(key, out var cachedEntry) && !cachedEntry.IsExpired)
        {
            return cachedEntry.CreateCopy();
        }

        if (!_vault.TryGet(key, out var secret) || secret is null)
        {
            _secretCache.TryRemove(key, out _);
            return null;
        }

        var newEntry = CacheEntry.FromSecret(secret, _cacheDuration);
        _secretCache[key] = newEntry;
        return newEntry.CreateCopy();
    }

    private void SaveSecretInternal(string key, SecureString value)
    {
        _vault.SaveSecret(key, value);
        _secretCache[key] = CacheEntry.FromSecret(value, _cacheDuration);
    }

    private void DeleteSecretInternal(string key)
    {
        _vault.DeleteSecret(key);
        _secretCache.TryRemove(key, out _);
    }

    private sealed class CacheEntry
    {
        private readonly SecureString _secret;
        private readonly DateTimeOffset _expiresAt;

        private CacheEntry(SecureString secret, DateTimeOffset expiresAt)
        {
            _secret = secret;
            _expiresAt = expiresAt;
        }

        public bool IsExpired => DateTimeOffset.UtcNow >= _expiresAt;

        public SecureString CreateCopy()
        {
            var copy = _secret.Copy();
            copy.MakeReadOnly();
            return copy;
        }

        public static CacheEntry FromSecret(SecureString secret, TimeSpan duration)
        {
            var copy = secret.Copy();
            copy.MakeReadOnly();
            return new CacheEntry(copy, DateTimeOffset.UtcNow.Add(duration));
        }
    }
}
