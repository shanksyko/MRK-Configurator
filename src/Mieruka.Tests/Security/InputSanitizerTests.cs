using System;
using System.IO;
using Mieruka.Core.Security;
using Xunit;

namespace Mieruka.Tests.Security;

public sealed class InputSanitizerTests : IDisposable
{
    private readonly string _baseDirectory;

    public InputSanitizerTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "MierukaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
    }

    [Fact]
    public void SanitizeSelector_AllowsValidSelector()
    {
        var sanitized = InputSanitizer.SanitizeSelector("  #main .item span  ");
        Assert.Equal("#main .item span", sanitized);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("expression(alert(1))")]
    [InlineData("onerror=alert(1)")]
    public void SanitizeSelector_RejectsDangerousPatterns(string selector)
    {
        Assert.Throws<ArgumentException>(() => InputSanitizer.SanitizeSelector(selector));
    }

    [Fact]
    public void SanitizeHost_NormalizesValidHost()
    {
        var sanitized = InputSanitizer.SanitizeHost("Example.COM.");
        Assert.Equal("example.com", sanitized);
    }

    [Theory]
    [InlineData("example.com:443")]
    [InlineData("example/com")]
    [InlineData("example\\com")]
    public void SanitizeHost_RejectsInvalidHosts(string host)
    {
        Assert.Throws<ArgumentException>(() => InputSanitizer.SanitizeHost(host));
    }

    [Fact]
    public void SanitizePath_NormalizesValidPath()
    {
        var sanitized = InputSanitizer.SanitizePath("logs/app.txt", _baseDirectory);
        Assert.StartsWith(_baseDirectory, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.True(sanitized.EndsWith(Path.Combine("logs", "app.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("..\\secret.txt")]
    [InlineData("..//secret.txt")]
    public void SanitizePath_RejectsTraversal(string path)
    {
        Assert.Throws<InvalidOperationException>(() => InputSanitizer.SanitizePath(path, _baseDirectory));
    }

    [Fact]
    public void SanitizePath_RejectsInvalidCharacters()
    {
        Assert.Throws<ArgumentException>(() => InputSanitizer.SanitizePath("logs\napp.txt", _baseDirectory));
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
            // best effort cleanup
        }
    }
}
