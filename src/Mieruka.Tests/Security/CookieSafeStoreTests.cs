using System;
using System.IO;
using System.Net;
using Mieruka.Core.Security;
using Xunit;

namespace Mieruka.Tests.Security;

public sealed class CookieSafeStoreTests : IDisposable
{
    private readonly string _baseDirectory;

    public CookieSafeStoreTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "mieruka-cookie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
    }

    [Fact]
    public void PutAndGet_Roundtrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new CookieSafeStore("tests", baseDirectory: _baseDirectory);
        var cookies = new[]
        {
            new Cookie("session", "abc123", "/", "example.com")
            {
                HttpOnly = true,
                Secure = true,
            },
        };

        Assert.True(store.Put("example.com", cookies, TimeSpan.FromMinutes(10)));
        Assert.True(store.TryGet("example.com", out var restored));

        Assert.Single(restored);
        var cookie = restored[0];
        Assert.Equal("session", cookie.Name);
        Assert.Equal("abc123", cookie.Value);
        Assert.Equal("/", cookie.Path);
        Assert.Equal("example.com", cookie.Domain);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);

        var payload = File.ReadAllBytes(store.ResolvePath("example.com"));
        Assert.NotEmpty(payload);
        Assert.DoesNotContain("abc123", Convert.ToHexString(payload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerateHosts_ReturnsExpectedEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new CookieSafeStore("tests", baseDirectory: _baseDirectory);
        var cookies = new[] { new Cookie("session", "value", "/", "example.com") };

        Assert.True(store.Put("example.com", cookies, TimeSpan.FromMinutes(5)));
        Assert.Contains("example.com", store.EnumerateHosts());

        store.Revoke("example.com");
        Assert.Empty(store.EnumerateHosts());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baseDirectory))
            {
                Directory.Delete(_baseDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
