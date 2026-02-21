using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Mieruka.Core.Security;

/// <summary>
/// Provides DPAPI backed storage for secrets that belong to the current user.
/// </summary>
public sealed class CredentialVault
{
    private const int FileFormatVersion = 1;
    private const string FileExtension = ".vault";
    private const string SiteKeyPrefix = "site";
    private const string UsernameSuffix = "username";
    private const string PasswordSuffix = "password";
    private const string TotpSuffix = "totp";

    /// <summary>
    /// Represents the logical version of a stored secret. Incrementing the value triggers
    /// an automatic re-encryption during the next read, allowing migrations of the payload format.
    /// </summary>
    public const int CurrentSecretVersion = 1;

    private readonly string _vaultDirectory;
    private readonly byte[] _entropy;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised whenever credentials associated with a site are modified.
    /// </summary>
    public event EventHandler<CredentialChangedEventArgs>? CredentialsChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialVault"/> class.
    /// </summary>
    /// <param name="applicationEntropy">Entropy bound to the application used during encryption.</param>
    /// <param name="vaultDirectory">Optional override for the storage directory.</param>
    public CredentialVault(string? applicationEntropy = null, string? vaultDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _vaultDirectory = vaultDirectory ?? Path.Combine(localAppData, "Mieruka", "secrets");

        Directory.CreateDirectory(_vaultDirectory);

        var entropySeed = string.IsNullOrWhiteSpace(applicationEntropy)
            ? "Mieruka.CredentialVault"
            : $"Mieruka.CredentialVault|{applicationEntropy.Trim()}";

        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes(entropySeed));
    }

    /// <summary>
    /// Saves a secret to the vault.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    /// <param name="secret">Secret that should be stored.</param>
    /// <param name="version">Logical version of the secret payload.</param>
    public void SaveSecret(string key, SecureString secret, int version = CurrentSecretVersion)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateKey(key);

        var chars = ExtractChars(secret);
        try
        {
            SaveSecretCore(key, chars, version);
            NotifyCredentialsChanged(key);
        }
        finally
        {
            if (chars.Length > 0)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
            }
        }
    }

    /// <summary>
    /// Saves a plain text secret to the vault.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    /// <param name="secret">Secret value in plain text.</param>
    /// <param name="version">Logical version of the secret payload.</param>
    public void SaveSecret(string key, string secret, int version = CurrentSecretVersion)
    {
        ArgumentNullException.ThrowIfNull(secret);

        using var secure = new SecureString();
        foreach (var ch in secret)
        {
            secure.AppendChar(ch);
        }

        secure.MakeReadOnly();
        SaveSecret(key, secure, version);
    }

    /// <summary>
    /// Determines whether a secret exists in the vault.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    public bool Exists(string key)
    {
        ValidateKey(key);
        var path = ResolveStoragePath(key);
        return File.Exists(path);
    }

    /// <summary>
    /// Attempts to retrieve a secret from the vault.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    /// <param name="secret">Secret value when found.</param>
    /// <returns><c>true</c> when the secret exists; otherwise, <c>false</c>.</returns>
    public bool TryGet(string key, out SecureString? secret)
    {
        try
        {
            secret = GetSecret(key);
            return true;
        }
        catch (SecretNotFoundException)
        {
            secret = null;
            return false;
        }
    }

    /// <summary>
    /// Persists a username associated with the provided site identifier.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <param name="username">Username stored in the vault.</param>
    public void SetUsername(string siteId, SecureString username)
    {
        ArgumentNullException.ThrowIfNull(username);
        SaveSecret(BuildSiteCredentialKey(siteId, UsernameSuffix), username);
    }

    /// <summary>
    /// Persists a password associated with the provided site identifier.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <param name="password">Password stored in the vault.</param>
    public void SetPassword(string siteId, SecureString password)
    {
        ArgumentNullException.ThrowIfNull(password);
        SaveSecret(BuildSiteCredentialKey(siteId, PasswordSuffix), password);
    }

    /// <summary>
    /// Persists a TOTP secret associated with the provided site identifier.
    /// </summary>
    /// <param name="siteId">Logical site identifier.</param>
    /// <param name="totp">TOTP seed stored in the vault.</param>
    public void SetTotp(string siteId, SecureString totp)
    {
        ArgumentNullException.ThrowIfNull(totp);
        SaveSecret(BuildSiteCredentialKey(siteId, TotpSuffix), totp);
    }

    /// <summary>
    /// Builds the username key associated with the provided site identifier.
    /// </summary>
    public static string BuildUsernameKey(string siteId)
        => BuildSiteCredentialKey(siteId, UsernameSuffix);

    /// <summary>
    /// Builds the password key associated with the provided site identifier.
    /// </summary>
    public static string BuildPasswordKey(string siteId)
        => BuildSiteCredentialKey(siteId, PasswordSuffix);

    /// <summary>
    /// Builds the TOTP key associated with the provided site identifier.
    /// </summary>
    public static string BuildTotpKey(string siteId)
        => BuildSiteCredentialKey(siteId, TotpSuffix);

    /// <summary>
    /// Retrieves a secret from the vault.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    /// <returns>Secret value stored for the specified key.</returns>
    /// <exception cref="SecretNotFoundException">Thrown when the secret could not be found.</exception>
    /// <exception cref="CredentialVaultCorruptedException">Thrown when the stored value is corrupted.</exception>
    public SecureString GetSecret(string key)
    {
        ValidateKey(key);

        var path = ResolveStoragePath(key);
        if (!File.Exists(path))
        {
            throw new SecretNotFoundException(key);
        }

        var sync = _locks.GetOrAdd(path, _ => new object());
        lock (sync)
        {
            byte[] raw = Array.Empty<byte>();
            try
            {
                raw = File.ReadAllBytes(path);
                if (raw.Length == 0)
                {
                    throw new CredentialVaultCorruptedException(key);
                }

                using var stream = new MemoryStream(raw, writable: false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                var version = reader.ReadInt32();
                if (version != FileFormatVersion)
                {
                    return ReadLegacySecret(key, raw);
                }

                var logicalVersion = reader.ReadInt32();
                var updatedAtTicks = reader.ReadInt64();
                var storedKeyHash = reader.ReadBytes(32);
                var cipherLength = reader.ReadInt32();
                var cipher = reader.ReadBytes(cipherLength);

                var expectedHash = ComputeKeyHash(key);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, storedKeyHash))
                {
                    throw new CredentialVaultCorruptedException(key);
                }

                var entropyBytes = (byte[])_entropy.Clone();
                byte[] plain = Array.Empty<byte>();
                GCHandle pinned = default;
                try
                {
                    plain = System.Security.Cryptography.ProtectedData.Unprotect(
                        cipher,
                        entropyBytes,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);

                    // Pin the decrypted buffer so the GC cannot relocate it
                    // before we have a chance to zero the contents.
                    if (plain.Length > 0)
                    {
                        pinned = GCHandle.Alloc(plain, GCHandleType.Pinned);
                    }

                    var secure = BuildSecureString(plain);
                    if (logicalVersion < CurrentSecretVersion)
                    {
                        var copy = secure.Copy();
                        copy.MakeReadOnly();
                        SaveSecret(key, copy, CurrentSecretVersion);
                    }

                    return secure;
                }
                finally
                {
                    if (plain.Length > 0)
                    {
                        Array.Clear(plain, 0, plain.Length);
                        CryptographicOperations.ZeroMemory(plain);
                    }

                    if (pinned.IsAllocated)
                    {
                        pinned.Free();
                    }

                    Array.Clear(entropyBytes, 0, entropyBytes.Length);
                    CryptographicOperations.ZeroMemory(entropyBytes);
                }
            }
            catch (EndOfStreamException)
            {
                // The version header matched but the rest of the file does not
                // conform to the new format.  This can happen when a legacy
                // DPAPI blob coincidentally starts with bytes that equal
                // FileFormatVersion.  Try interpreting the full blob as a
                // legacy secret before giving up.
                try
                {
                    return ReadLegacySecret(key, raw);
                }
                catch
                {
                    DeleteSecret(key);
                    throw new CredentialVaultCorruptedException(key);
                }
            }
            catch (CryptographicException)
            {
                // Same fallback for decryption errors — the file may be a
                // legacy blob whose header bytes happened to match the
                // current format version.
                try
                {
                    return ReadLegacySecret(key, raw);
                }
                catch
                {
                    DeleteSecret(key);
                    throw new CredentialVaultCorruptedException(key);
                }
            }
            catch (CredentialVaultCorruptedException)
            {
                // Hash mismatch or structural error — may still be a legacy
                // blob whose first bytes accidentally matched the format
                // version.  Attempt the legacy path before declaring corruption.
                if (raw.Length > 0)
                {
                    try
                    {
                        return ReadLegacySecret(key, raw);
                    }
                    catch
                    {
                        // Legacy path also failed — genuinely corrupted.
                    }
                }

                DeleteSecret(key);
                throw;
            }
        }
    }

    /// <summary>
    /// Deletes a secret from the vault. The operation succeeds even if the secret does not exist.
    /// </summary>
    /// <param name="key">Logical name of the secret.</param>
    public void DeleteSecret(string key)
    {
        ValidateKey(key);
        var path = ResolveStoragePath(key);
        var sync = _locks.GetOrAdd(path, _ => new object());

        lock (sync)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        NotifyCredentialsChanged(key);
    }

    internal string ResolveStoragePath(string key)
    {
        ValidateKey(key);
        var fileName = $"{Convert.ToHexString(ComputeKeyHash(key))}{FileExtension}";
        return Path.Combine(_vaultDirectory, fileName);
    }

    internal ReadOnlySpan<byte> EntropySpan => _entropy;

    private void SaveSecretCore(string key, ReadOnlySpan<char> chars, int version)
    {
        var path = ResolveStoragePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sync = _locks.GetOrAdd(path, _ => new object());
        lock (sync)
        {
            var charsArray = chars.ToArray();
            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(charsArray);
                var entropyBytes = (byte[])_entropy.Clone();
                byte[] cipher = Array.Empty<byte>();
                try
                {
                    cipher = System.Security.Cryptography.ProtectedData.Protect(
                        plainBytes,
                        entropyBytes,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);

                    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

                    writer.Write(FileFormatVersion);
                    writer.Write(version);
                    writer.Write(DateTimeOffset.UtcNow.UtcTicks);
                    writer.Write(ComputeKeyHash(key));
                    writer.Write(cipher.Length);
                    writer.Write(cipher);
                }
                finally
                {
                    if (cipher.Length > 0)
                    {
                        Array.Clear(cipher, 0, cipher.Length);
                        CryptographicOperations.ZeroMemory(cipher);
                    }

                    if (plainBytes.Length > 0)
                    {
                        Array.Clear(plainBytes, 0, plainBytes.Length);
                        CryptographicOperations.ZeroMemory(plainBytes);
                    }

                    Array.Clear(entropyBytes, 0, entropyBytes.Length);
                    CryptographicOperations.ZeroMemory(entropyBytes);
                }
            }
            finally
            {
                if (charsArray.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(charsArray.AsSpan()));
                }
            }
        }
    }

    private SecureString ReadLegacySecret(string key, byte[] raw)
    {
        var cipher = raw;
        var entropyBytes = (byte[])_entropy.Clone();
        byte[] plain = Array.Empty<byte>();
        GCHandle pinned = default;
        try
        {
            plain = System.Security.Cryptography.ProtectedData.Unprotect(
                cipher,
                entropyBytes,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);

            if (plain.Length > 0)
            {
                pinned = GCHandle.Alloc(plain, GCHandleType.Pinned);
            }

            var secure = BuildSecureString(plain);
            var copy = secure.Copy();
            copy.MakeReadOnly();
            SaveSecret(key, copy, CurrentSecretVersion);
            return secure;
        }
        finally
        {
            if (plain.Length > 0)
            {
                Array.Clear(plain, 0, plain.Length);
                CryptographicOperations.ZeroMemory(plain);
            }

            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            Array.Clear(entropyBytes, 0, entropyBytes.Length);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A key must be provided.", nameof(key));
        }

        if (key.Length > 128)
        {
            throw new ArgumentException("Secret keys must not exceed 128 characters.", nameof(key));
        }
    }

    private static string BuildSiteCredentialKey(string siteId, string suffix)
    {
        ValidateSiteId(siteId);
        return $"{SiteKeyPrefix}:{siteId}:{suffix}";
    }

    private static bool TryParseSiteCredentialKey(string key, out string siteId, out CredentialKind kind)
    {
        siteId = string.Empty;
        kind = default;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!string.Equals(parts[0], SiteKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        siteId = parts[1];
        kind = parts[2] switch
        {
            UsernameSuffix => CredentialKind.Username,
            PasswordSuffix => CredentialKind.Password,
            TotpSuffix => CredentialKind.Totp,
            _ => default,
        };

        return kind != default;
    }

    private void NotifyCredentialsChanged(string key)
    {
        if (!TryParseSiteCredentialKey(key, out var siteId, out var kind))
        {
            return;
        }

        CredentialsChanged?.Invoke(this, new CredentialChangedEventArgs(siteId, kind));
    }

    private static void ValidateSiteId(string siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            throw new ArgumentException("Site identifier is required.", nameof(siteId));
        }
    }

    private static char[] ExtractChars(SecureString value)
    {
        if (value.Length == 0)
        {
            return Array.Empty<char>();
        }

        IntPtr pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
            var buffer = new char[value.Length];
            Marshal.Copy(pointer, buffer, 0, value.Length);
            return buffer;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }

    private static SecureString BuildSecureString(byte[] plain)
    {
        var charCount = Encoding.UTF8.GetCharCount(plain);
        if (charCount == 0)
        {
            var empty = new SecureString();
            empty.MakeReadOnly();
            return empty;
        }

        var buffer = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            Encoding.UTF8.GetChars(plain, 0, plain.Length, buffer, 0);
            var secure = new SecureString();
            for (var i = 0; i < charCount; i++)
            {
                secure.AppendChar(buffer[i]);
            }

            secure.MakeReadOnly();
            return secure;
        }
        finally
        {
            Array.Clear(buffer, 0, charCount);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static byte[] ComputeKeyHash(string key)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}

/// <summary>
/// Represents the type of credential stored for a site.
/// </summary>
public enum CredentialKind
{
    None = 0,
    Username,
    Password,
    Totp,
}

/// <summary>
/// Event arguments describing a credential mutation.
/// </summary>
public sealed class CredentialChangedEventArgs : EventArgs
{
    public CredentialChangedEventArgs(string siteId, CredentialKind kind)
    {
        SiteId = siteId;
        Kind = kind;
    }

    public string SiteId { get; }

    public CredentialKind Kind { get; }
}

/// <summary>
/// Base exception for credential vault errors.
/// </summary>
public class CredentialVaultException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialVaultException"/> class.
    /// </summary>
    public CredentialVaultException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Exception thrown when a secret is not found.
/// </summary>
public sealed class SecretNotFoundException : CredentialVaultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecretNotFoundException"/> class.
    /// </summary>
    public SecretNotFoundException(string key)
        : base($"Secret '{key}' was not found.")
    {
    }
}

/// <summary>
/// Exception thrown when the stored secret is corrupted and cannot be decrypted.
/// </summary>
public sealed class CredentialVaultCorruptedException : CredentialVaultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialVaultCorruptedException"/> class.
    /// </summary>
    public CredentialVaultCorruptedException(string key, Exception? inner = null)
        : base($"Stored secret '{key}' is corrupted and was removed.", inner)
    {
    }
}
