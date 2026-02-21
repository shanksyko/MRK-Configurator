using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

#nullable enable

namespace Mieruka.Core.Security;

/// <summary>
/// Provides encrypted persistence for cookies grouped by host.
/// </summary>
public sealed class CookieSafeStore
{
    private const int CurrentVersion = 1;
    private const string FileExtension = ".cookies";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _baseDirectory;
    private readonly byte[] _entropy;
    private readonly Func<bool> _thirdPartyDisabled;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly AuditLog? _auditLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="CookieSafeStore"/> class.
    /// </summary>
    /// <param name="applicationEntropy">Entropy associated with the application.</param>
    /// <param name="thirdPartyCookiesDisabled">Delegate indicating whether 3rd party cookies are disabled.</param>
    /// <param name="baseDirectory">Optional override for the storage directory.</param>
    /// <param name="auditLog">Audit log used to register operations.</param>
    public CookieSafeStore(
        string? applicationEntropy = null,
        Func<bool>? thirdPartyCookiesDisabled = null,
        string? baseDirectory = null,
        AuditLog? auditLog = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseDirectory = baseDirectory ?? Path.Combine(localAppData, "Mieruka", "cookies");
        Directory.CreateDirectory(_baseDirectory);

        var entropySeed = string.IsNullOrWhiteSpace(applicationEntropy)
            ? "Mieruka.CookieSafeStore"
            : $"Mieruka.CookieSafeStore|{applicationEntropy.Trim()}";
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes(entropySeed));
        _thirdPartyDisabled = thirdPartyCookiesDisabled ?? (() => false);
        _auditLog = auditLog;
    }

    /// <summary>
    /// Stores cookies associated with a host.
    /// </summary>
    /// <param name="host">Host that owns the cookies.</param>
    /// <param name="cookies">Cookies that should be persisted.</param>
    /// <param name="ttl">Time to live for the stored entry.</param>
    /// <param name="isThirdParty">Indicates whether the cookies originate from a third-party context.</param>
    /// <returns><c>true</c> when the cookies have been stored, otherwise <c>false</c>.</returns>
    public bool Put(string host, IEnumerable<Cookie> cookies, TimeSpan ttl, bool isThirdParty = false)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        var normalizedHost = InputSanitizer.SanitizeHost(host);
        if (string.IsNullOrEmpty(normalizedHost))
        {
            throw new ArgumentException("Host must be specified.", nameof(host));
        }

        if (isThirdParty && _thirdPartyDisabled())
        {
            _auditLog?.RecordCookieBlocked(normalizedHost);
            return false;
        }

        var payload = new CookieFilePayload
        {
            Version = CurrentVersion,
            Host = normalizedHost,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl),
        };

        foreach (var cookie in cookies)
        {
            if (cookie is null)
            {
                continue;
            }

            payload.Cookies.Add(new SerializableCookie
            {
                Name = cookie.Name ?? string.Empty,
                Value = cookie.Value ?? string.Empty,
                Path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                Domain = cookie.Domain ?? normalizedHost,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                Expires = cookie.Expires != DateTime.MinValue ? cookie.Expires : null,
            });
        }

        // Serialize directly to UTF-8 bytes, avoiding an intermediate
        // immutable string that would linger in the managed heap.
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        byte[] cipher = Array.Empty<byte>();
        try
        {
            cipher = Protect(plainBytes);
        }
        finally
        {
            if (plainBytes.Length > 0)
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        try
        {
            var path = ResolvePath(normalizedHost);
            var sync = _locks.GetOrAdd(path, _ => new object());

            lock (sync)
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(cipher, 0, cipher.Length);
            }

            _auditLog?.RecordCookieStored(normalizedHost, payload.Cookies.Count);
            return true;
        }
        finally
        {
            if (cipher.Length > 0)
            {
                Array.Clear(cipher, 0, cipher.Length);
                CryptographicOperations.ZeroMemory(cipher);
            }
        }
    }

    /// <summary>
    /// Attempts to read cookies for the supplied host.
    /// </summary>
    /// <param name="host">Target host.</param>
    /// <param name="cookies">Recovered cookies.</param>
    /// <param name="isThirdParty">Indicates whether the cookies should be treated as third-party.</param>
    /// <returns><c>true</c> when the cookies were retrieved successfully.</returns>
    public bool TryGet(string host, out IReadOnlyList<Cookie> cookies, bool isThirdParty = false)
    {
        var normalizedHost = InputSanitizer.SanitizeHost(host);
        if (string.IsNullOrEmpty(normalizedHost))
        {
            cookies = Array.Empty<Cookie>();
            return false;
        }

        if (isThirdParty && _thirdPartyDisabled())
        {
            cookies = Array.Empty<Cookie>();
            _auditLog?.RecordCookieBlocked(normalizedHost);
            return false;
        }

        var path = ResolvePath(normalizedHost);
        if (!File.Exists(path))
        {
            cookies = Array.Empty<Cookie>();
            return false;
        }

        var sync = _locks.GetOrAdd(path, _ => new object());
        lock (sync)
        {
            try
            {
                byte[] cipher = Array.Empty<byte>();
                try
                {
                    cipher = File.ReadAllBytes(path);
                    byte[] jsonBytes = Array.Empty<byte>();
                    try
                    {
                        var entropyBytes = (byte[])_entropy.Clone();
                        try
                        {
                            jsonBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                                cipher,
                                entropyBytes,
                                System.Security.Cryptography.DataProtectionScope.CurrentUser);
                        }
                        finally
                        {
                            Array.Clear(entropyBytes, 0, entropyBytes.Length);
                            CryptographicOperations.ZeroMemory(entropyBytes);
                        }

                        var json = Encoding.UTF8.GetString(jsonBytes);
                        var payload = JsonSerializer.Deserialize<CookieFilePayload>(json, SerializerOptions);
                        if (payload is null)
                        {
                            cookies = Array.Empty<Cookie>();
                            return false;
                        }

                        if (payload.ExpiresAt <= DateTimeOffset.UtcNow)
                        {
                            cookies = Array.Empty<Cookie>();
                            Revoke(normalizedHost);
                            return false;
                        }

                        var list = new List<Cookie>(payload.Cookies.Count);
                        foreach (var cookie in payload.Cookies)
                        {
                            var restored = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                            {
                                Secure = cookie.Secure,
                                HttpOnly = cookie.HttpOnly,
                            };

                            if (cookie.Expires.HasValue)
                            {
                                restored.Expires = cookie.Expires.Value.UtcDateTime;
                            }

                            list.Add(restored);
                        }

                        cookies = list;
                        _auditLog?.RecordCookieRestored(normalizedHost, list.Count);
                        return true;
                    }
                    finally
                    {
                        if (jsonBytes.Length > 0)
                        {
                            Array.Clear(jsonBytes, 0, jsonBytes.Length);
                            CryptographicOperations.ZeroMemory(jsonBytes);
                        }
                    }
                }
                finally
                {
                    if (cipher.Length > 0)
                    {
                        Array.Clear(cipher, 0, cipher.Length);
                        CryptographicOperations.ZeroMemory(cipher);
                    }
                }
            }
            catch (CryptographicException)
            {
                cookies = Array.Empty<Cookie>();
                Revoke(normalizedHost);
                return false;
            }
            catch (JsonException)
            {
                cookies = Array.Empty<Cookie>();
                Revoke(normalizedHost);
                return false;
            }
        }
    }

    /// <summary>
    /// Removes stored cookies for the provided host.
    /// </summary>
    /// <param name="host">Host name.</param>
    public void Revoke(string host)
    {
        var normalizedHost = InputSanitizer.SanitizeHost(host);
        if (string.IsNullOrEmpty(normalizedHost))
        {
            return;
        }

        var path = ResolvePath(normalizedHost);
        var sync = _locks.GetOrAdd(path, _ => new object());
        lock (sync)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _auditLog?.RecordCookieRevoked(normalizedHost);
            }
        }
    }

    /// <summary>
    /// Cleans up expired cookie entries.
    /// </summary>
    public void PurgeExpired()
    {
        foreach (var file in Directory.EnumerateFiles(_baseDirectory, $"*{FileExtension}"))
        {
            var sync = _locks.GetOrAdd(file, _ => new object());
            lock (sync)
            {
                try
                {
                    byte[] cipher = Array.Empty<byte>();
                    try
                    {
                        cipher = File.ReadAllBytes(file);
                        byte[] jsonBytes = Array.Empty<byte>();
                        try
                        {
                            var entropyBytes = (byte[])_entropy.Clone();
                            try
                            {
                                jsonBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                                    cipher,
                                    entropyBytes,
                                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                            }
                            finally
                            {
                                Array.Clear(entropyBytes, 0, entropyBytes.Length);
                                CryptographicOperations.ZeroMemory(entropyBytes);
                            }

                            var json = Encoding.UTF8.GetString(jsonBytes);
                            var payload = JsonSerializer.Deserialize<CookieFilePayload>(json, SerializerOptions);
                            if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow)
                            {
                                File.Delete(file);
                            }
                        }
                        finally
                        {
                            if (jsonBytes.Length > 0)
                            {
                                Array.Clear(jsonBytes, 0, jsonBytes.Length);
                                CryptographicOperations.ZeroMemory(jsonBytes);
                            }
                        }
                    }
                    finally
                    {
                        if (cipher.Length > 0)
                        {
                            Array.Clear(cipher, 0, cipher.Length);
                            CryptographicOperations.ZeroMemory(cipher);
                        }
                    }
                }
                catch (Exception)
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <summary>
    /// Enumerates the hosts currently stored in the cookie repository.
    /// </summary>
    public IReadOnlyList<string> EnumerateHosts()
    {
        var hosts = new List<string>();

        foreach (var file in Directory.EnumerateFiles(_baseDirectory, $"*{FileExtension}"))
        {
            var sync = _locks.GetOrAdd(file, _ => new object());
            lock (sync)
            {
                try
                {
                    byte[] cipher = Array.Empty<byte>();
                    try
                    {
                        cipher = File.ReadAllBytes(file);
                        byte[] jsonBytes = Array.Empty<byte>();
                        try
                        {
                            var entropyBytes = (byte[])_entropy.Clone();
                            try
                            {
                                jsonBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                                    cipher,
                                    entropyBytes,
                                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                            }
                            finally
                            {
                                Array.Clear(entropyBytes, 0, entropyBytes.Length);
                                CryptographicOperations.ZeroMemory(entropyBytes);
                            }

                            var json = Encoding.UTF8.GetString(jsonBytes);
                            var payload = JsonSerializer.Deserialize<CookieFilePayload>(json, SerializerOptions);
                            if (payload is not null && payload.ExpiresAt > DateTimeOffset.UtcNow)
                            {
                                hosts.Add(payload.Host);
                            }
                        }
                        finally
                        {
                            if (jsonBytes.Length > 0)
                            {
                                Array.Clear(jsonBytes, 0, jsonBytes.Length);
                                CryptographicOperations.ZeroMemory(jsonBytes);
                            }
                        }
                    }
                    finally
                    {
                        if (cipher.Length > 0)
                        {
                            Array.Clear(cipher, 0, cipher.Length);
                            CryptographicOperations.ZeroMemory(cipher);
                        }
                    }
                }
                catch
                {
                    // Ignore corrupted entries during enumeration; they will be purged on next cleanup.
                }
            }
        }

        return hosts;
    }

    internal string ResolvePath(string host)
    {
        var fileName = $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host)))}{FileExtension}";
        return Path.Combine(_baseDirectory, fileName);
    }

    private byte[] Protect(byte[] payload)
    {
        var entropyBytes = (byte[])_entropy.Clone();
        try
        {
            return System.Security.Cryptography.ProtectedData.Protect(
                payload,
                entropyBytes,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
        }
        finally
        {
            Array.Clear(entropyBytes, 0, entropyBytes.Length);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    private sealed record SerializableCookie
    {
        public string Name { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public string Path { get; init; } = "/";

        public string Domain { get; init; } = string.Empty;

        public bool Secure { get; init; }

        public bool HttpOnly { get; init; }

        public DateTimeOffset? Expires { get; init; }
    }

    private sealed record CookieFilePayload
    {
        public int Version { get; init; }

        public string Host { get; init; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; init; }

        public List<SerializableCookie> Cookies { get; } = new();
    }
}
