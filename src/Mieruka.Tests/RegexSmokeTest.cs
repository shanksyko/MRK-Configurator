using System;
using System.Text.RegularExpressions;
using Mieruka.Core.Security;
using Xunit;

namespace Mieruka.Tests;

public sealed class RegexSmokeTest
{
    [Theory]
    [InlineData("body", true)]
    [InlineData("div > .class", true)]
    [InlineData("<script>", false)]
    public void SelectorRegexValidates(string input, bool shouldMatch)
    {
        var result = TrySanitizeSelector(input);
        Assert.Equal(shouldMatch, result);
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("sub.example.com", true)]
    [InlineData("bad host!", false)]
    public void HostRegexValidates(string input, bool shouldMatch)
    {
        var result = TrySanitizeHost(input);
        Assert.Equal(shouldMatch, result);
    }

    [Fact]
    public void EmailRedactionWorks()
    {
        var result = Redaction.Redact("contact user@example.com please");
        Assert.DoesNotContain("user@example.com", result);
        Assert.Contains("***@***", result);
    }

    [Fact]
    public void TokenRedactionWorks()
    {
        var result = Redaction.Redact("bearer=abcdefghi");
        Assert.DoesNotContain("abcdefghi", result);
        Assert.Contains("<redacted>", result);
    }

    [Fact]
    public void GuidRedactionWorks()
    {
        var result = Redaction.Redact("id=00000000-0000-0000-0000-000000000000");
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", result);
        Assert.Contains("<id>", result);
    }

    private static bool TrySanitizeSelector(string value)
    {
        try
        {
            InputSanitizer.SanitizeSelector(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TrySanitizeHost(string value)
    {
        try
        {
            InputSanitizer.SanitizeHost(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
