using System;
using System.Security;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Xunit;

namespace Mieruka.Tests;

public sealed class BrowserTypeTests
{
    [Theory]
    [InlineData(BrowserType.Chrome)]
    [InlineData(BrowserType.Edge)]
    [InlineData(BrowserType.Firefox)]
    [InlineData(BrowserType.Brave)]
    public void BrowserType_AllValues_CanBeEnumerated(BrowserType browser)
    {
        Assert.True(Enum.IsDefined(browser));
    }

    [Fact]
    public void BrowserType_Count_IsFour()
    {
        var values = Enum.GetValues<BrowserType>();
        Assert.Equal(4, values.Length);
    }
}

public sealed class LoginProfileTests
{
    [Fact]
    public void LoginProfile_DefaultTimeout_IsFifteenSeconds()
    {
        var profile = new LoginProfile();
        Assert.Equal(15, profile.TimeoutSeconds);
    }

    [Fact]
    public void LoginProfile_Password_IsNullByDefault()
    {
        var profile = new LoginProfile();
        Assert.Null(profile.Password);
    }

    [Fact]
    public void LoginProfile_SsoHints_IsEmptyByDefault()
    {
        var profile = new LoginProfile();
        Assert.Empty(profile.SsoHints);
    }

    [Fact]
    public void LoginProfile_WithRecord_CreatesModifiedCopy()
    {
        var original = new LoginProfile { Username = "user1" };
        var modified = original with { Username = "user2" };

        Assert.Equal("user1", original.Username);
        Assert.Equal("user2", modified.Username);
    }
}

public sealed class CredentialVaultTests
{
    [Fact]
    public void BuildUsernameKey_ContainsSitePrefix()
    {
        var key = CredentialVault.BuildUsernameKey("mysite");
        Assert.Contains("mysite", key);
        Assert.StartsWith("site:", key);
    }

    [Fact]
    public void BuildPasswordKey_ContainsSitePrefix()
    {
        var key = CredentialVault.BuildPasswordKey("mysite");
        Assert.Contains("mysite", key);
        Assert.Contains("password", key);
    }

    [Fact]
    public void BuildTotpKey_ContainsSitePrefix()
    {
        var key = CredentialVault.BuildTotpKey("mysite");
        Assert.Contains("mysite", key);
        Assert.Contains("totp", key);
    }

    [Fact]
    public void SaveAndRetrieve_RoundTrips_SecureString()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI is Windows-only.
        }

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vault-test-{Guid.NewGuid():N}");
        try
        {
            var vault = new CredentialVault(vaultDirectory: tempDir);
            var secret = new SecureString();
            foreach (var ch in "TestSecret123!")
            {
                secret.AppendChar(ch);
            }

            secret.MakeReadOnly();

            vault.SaveSecret("test-key", secret);

            Assert.True(vault.Exists("test-key"));
            Assert.True(vault.TryGet("test-key", out var retrieved));
            Assert.NotNull(retrieved);
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

    [Fact]
    public void Exists_ReturnsFalse_ForMissingKey()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vault-test-{Guid.NewGuid():N}");
        try
        {
            var vault = new CredentialVault(vaultDirectory: tempDir);
            Assert.False(vault.Exists("nonexistent-key"));
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForMissingKey()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vault-test-{Guid.NewGuid():N}");
        try
        {
            var vault = new CredentialVault(vaultDirectory: tempDir);
            Assert.False(vault.TryGet("nonexistent", out _));
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    [Fact]
    public void DeleteSecret_DoesNotThrow_ForMissingKey()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vault-test-{Guid.NewGuid():N}");
        try
        {
            var vault = new CredentialVault(vaultDirectory: tempDir);
            vault.DeleteSecret("does-not-exist");
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}
