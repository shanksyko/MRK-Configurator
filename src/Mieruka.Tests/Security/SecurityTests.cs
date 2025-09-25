using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Policy;
using Xunit;

namespace Mieruka.Tests.Security;

public sealed class SecurityTests : IDisposable
{
    private readonly string _tempDirectory;

    public SecurityTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mieruka-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void CredentialVault_Roundtrip()
    {
        var vault = new CredentialVault("tests", Path.Combine(_tempDirectory, "vault"));
        var secret = CreateSecureString("super-secret");
        vault.SaveSecret("api-key", secret);

        var recovered = vault.GetSecret("api-key");
        Assert.Equal("super-secret", ToUnsecureString(recovered));

        var path = vault.ResolveStoragePath("api-key");
        var payload = File.ReadAllBytes(path);
        Assert.NotEmpty(payload);
        Assert.DoesNotContain("super-secret", Convert.ToHexString(payload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CredentialVault_SiteKeyConventions()
    {
        Assert.Equal("site:alpha:username", CredentialVault.BuildUsernameKey("alpha"));
        Assert.Equal("site:alpha:password", CredentialVault.BuildPasswordKey("alpha"));
        Assert.Equal("site:alpha:totp", CredentialVault.BuildTotpKey("alpha"));
    }

    [Fact]
    public void CredentialVault_MigratesLegacyEntries()
    {
        var vault = new CredentialVault("tests", Path.Combine(_tempDirectory, "vault"));
        var path = vault.ResolveStoragePath("legacy");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var payload = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes("legacy"),
            vault.EntropySpan.ToArray(),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, payload);

        var recovered = vault.GetSecret("legacy");
        Assert.Equal("legacy", ToUnsecureString(recovered));

        // Ensure the secret can be read again using the new format.
        var recoveredAgain = vault.GetSecret("legacy");
        Assert.Equal("legacy", ToUnsecureString(recoveredAgain));
    }

    [Fact]
    public void CredentialVault_CorruptionIsHandled()
    {
        var vault = new CredentialVault("tests", Path.Combine(_tempDirectory, "vault"));
        vault.SaveSecret("broken", "value");
        var path = vault.ResolveStoragePath("broken");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

        Assert.Throws<CredentialVaultCorruptedException>(() => vault.GetSecret("broken"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SecretsProvider_StoresAndRetrievesCredentials()
    {
        var vault = new CredentialVault("tests", Path.Combine(_tempDirectory, "vault"));
        var cookies = new CookieSafeStore("tests", baseDirectory: Path.Combine(_tempDirectory, "cookies"));
        var provider = new SecretsProvider(vault, cookies, TimeSpan.FromMinutes(5));

        using var username = CreateSecureString("user@example.com");
        using var password = CreateSecureString("p@ssw0rd");
        using var totp = CreateSecureString("123456");

        provider.SaveCredentials("alpha", username, password);
        provider.SetTotp("alpha", totp);

        var cachedUser = provider.GetUsernameFor("alpha");
        var cachedPass = provider.GetPasswordFor("alpha");
        var cachedTotp = provider.GetTotpFor("alpha");

        Assert.NotNull(cachedUser);
        Assert.NotNull(cachedPass);
        Assert.NotNull(cachedTotp);
        Assert.Equal("user@example.com", ToUnsecureString(cachedUser!));
        Assert.Equal("p@ssw0rd", ToUnsecureString(cachedPass!));
        Assert.Equal("123456", ToUnsecureString(cachedTotp!));

        provider.Delete("alpha");
        Assert.Null(provider.GetUsernameFor("alpha"));
        Assert.Null(provider.GetPasswordFor("alpha"));
        Assert.Null(provider.GetTotpFor("alpha"));
    }

    [Fact]
    public void Redaction_RemovesSensitiveData()
    {
        var input = "Usuário test@example.com com token=abc123456 e password=senha";
        var output = Redaction.Redact(input);
        Assert.DoesNotContain("test@example.com", output);
        Assert.DoesNotContain("abc123456", output);
        Assert.DoesNotContain("senha", output);

        var entry = new HttpLogEntry(
            "GET",
            "https://example.com",
            new System.Collections.Generic.Dictionary<string, string> { ["Authorization"] = "Bearer supertoken" },
            "token=secreto");
        var sanitized = Redaction.Redact(entry);
        Assert.DoesNotContain("supertoken", sanitized.Headers["Authorization"]);
        Assert.DoesNotContain("secreto", sanitized.Body);
    }

    [Fact]
    public void Allowlist_BlocksAndAudits()
    {
        var audit = new AuditLog(_tempDirectory);
        var allowlist = new UrlAllowlist(audit);
        allowlist.Add("example.com");

        Assert.True(allowlist.IsAllowed(new Uri("https://example.com")));
        Assert.False(allowlist.IsAllowed(new Uri("https://blocked.com")));

        var logPath = Path.Combine(_tempDirectory, "audit.jsonl");
        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("allowlist_block", content);
    }

    [Fact]
    public void IntegrityService_FailsOnMismatch()
    {
        var file = Path.Combine(_tempDirectory, "driver.exe");
        File.WriteAllText(file, "driver");
        var expectedHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("different")));
        var manifest = new IntegrityManifest(1, new[] { new FileIntegrityExpectation(file, expectedHash) });

        var service = new IntegrityService(new AuditLog(_tempDirectory));
        Assert.Throws<IntegrityViolationException>(() => service.Validate(manifest));
    }

    [Fact]
    public void DriverVersionGuard_ThrowsOnMismatch()
    {
        var guard = new DriverVersionGuard();
        Assert.Throws<DriverVersionMismatchException>(() => guard.EnsureCompatible(new Version(120, 1), new Version(118, 0)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static SecureString CreateSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var ch in value)
        {
            secure.AppendChar(ch);
        }

        secure.MakeReadOnly();
        return secure;
    }

    private static string ToUnsecureString(SecureString value)
    {
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToBSTR(value);
            return Marshal.PtrToStringBSTR(pointer) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(pointer);
            }
        }
    }
}
