using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Mieruka.Core.Models;

namespace Mieruka.Automation.Login;

/// <summary>
/// Provides helpers to securely store and retrieve credentials using the Windows Credential Manager.
/// </summary>
public sealed class CredentialVault
{
    private const string VaultPrefix = "vault:";
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;

    private readonly string _applicationName;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialVault"/> class.
    /// </summary>
    /// <param name="applicationName">Application name used to scope credential entries.</param>
    public CredentialVault(string applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(applicationName));
        }

        _applicationName = applicationName;
    }

    /// <summary>
    /// Stores a credential in the Windows Credential Manager.
    /// </summary>
    /// <param name="key">Identifier used to reference the credential.</param>
    /// <param name="username">Username component of the credential.</param>
    /// <param name="password">Password component of the credential.</param>
    public void Save(string key, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Credential vault is only supported on Windows.");
        }

        SaveInternal(NormalizeKey(key), username, password);
    }

    /// <summary>
    /// Attempts to retrieve a credential from the Windows Credential Manager.
    /// </summary>
    /// <param name="key">Identifier used to reference the credential.</param>
    /// <param name="credential">Credential retrieved from the vault.</param>
    /// <returns><c>true</c> when the credential exists, otherwise <c>false</c>.</returns>
    public bool TryGet(string key, out NetworkCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        credential = default!;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return TryGetInternal(NormalizeKey(key), out credential);
    }

    /// <summary>
    /// Removes a credential from the Windows Credential Manager.
    /// </summary>
    /// <param name="key">Identifier used to reference the credential.</param>
    public void Delete(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Credential vault is only supported on Windows.");
        }

        DeleteInternal(NormalizeKey(key));
    }

    /// <summary>
    /// Resolves credentials referenced by a <see cref="LoginProfile"/> instance.
    /// </summary>
    /// <param name="profile">Profile that may contain vault references.</param>
    /// <returns>A profile with concrete credentials.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a referenced entry cannot be located.</exception>
    public LoginProfile Resolve(LoginProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var (requiresVault, key, username, password) = ExtractReferences(profile);

        if (!requiresVault)
        {
            return profile;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Credential vault is only supported on Windows.");
        }

        if (!TryGetInternal(key!, out var credential))
        {
            throw new InvalidOperationException($"Credential vault entry '{key}' was not found.");
        }

        var resolvedUsername = username ?? credential.UserName ?? string.Empty;
        var resolvedPassword = password ?? credential.Password ?? string.Empty;

        return profile with
        {
            Username = resolvedUsername,
            Password = resolvedPassword,
        };
    }

    private static (bool RequiresVault, string? Key, string? Username, string? Password) ExtractReferences(LoginProfile profile)
    {
        string? key = null;
        string? username = profile.Username;
        string? password = profile.Password;
        var requiresVault = false;

        if (TryParseVaultReference(username, out var userKey))
        {
            key = userKey;
            username = null;
            requiresVault = true;
        }

        if (TryParseVaultReference(password, out var passKey))
        {
            if (key is null)
            {
                key = passKey;
            }
            else if (!string.Equals(key, passKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Login profile references different vault entries for username and password.");
            }

            password = null;
            requiresVault = true;
        }

        return (requiresVault, key, username, password);
    }

    private static bool TryParseVaultReference(string? value, out string? key)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(VaultPrefix, StringComparison.OrdinalIgnoreCase))
        {
            key = value[VaultPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(key))
            {
                return true;
            }
        }

        key = null;
        return false;
    }

    private string NormalizeKey(string key)
    {
        return $"{_applicationName}/{key.Trim()}";
    }

    [SupportedOSPlatform("windows")]
    private static void SaveInternal(string targetName, string username, string password)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var credential = new NativeCredential
        {
            AttributeCount = 0,
            Attributes = IntPtr.Zero,
            Comment = null,
            TargetAlias = null,
            Type = CredentialTypeGeneric,
            Persist = CredentialPersistLocalMachine,
            CredentialBlobSize = (uint)passwordBytes.Length,
            TargetName = targetName,
            UserName = username,
        };

        credential.CredentialBlob = Marshal.AllocCoTaskMem(passwordBytes.Length);

        try
        {
            Marshal.Copy(passwordBytes, 0, credential.CredentialBlob, passwordBytes.Length);

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(credential.CredentialBlob);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetInternal(string targetName, out NetworkCredential credential)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPtr))
        {
            credential = default!;
            return false;
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            var username = native.UserName ?? string.Empty;
            var password = string.Empty;

            if (native.CredentialBlob != IntPtr.Zero && native.CredentialBlobSize > 0)
            {
                var passwordBytes = new byte[native.CredentialBlobSize];
                Marshal.Copy(native.CredentialBlob, passwordBytes, 0, passwordBytes.Length);
                password = Encoding.Unicode.GetString(passwordBytes).TrimEnd('\0');
            }

            credential = new NetworkCredential(username, password);
            return true;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteInternal(string targetName)
    {
        if (!CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is not 1168) // ERROR_NOT_FOUND
            {
                throw new Win32Exception(error);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
