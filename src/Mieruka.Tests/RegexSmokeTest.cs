using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Mieruka.Core.Security;
using Xunit;

namespace Mieruka.Tests;

public sealed class RegexSmokeTest
{
    public static IEnumerable<object[]> CriticalRegexes()
    {
        yield return new object[] { typeof(InputSanitizer), "SelectorRegex", "body" };
        yield return new object[] { typeof(InputSanitizer), "HostRegex", "example" };
        yield return new object[] { typeof(Redaction), "EmailRegex", "user@example.com" };
        yield return new object[] { typeof(Redaction), "TokenRegex", "bearer=abcdefghi" };
        yield return new object[] { typeof(Redaction), "GuidRegex", "00000000-0000-0000-0000-000000000000" };
    }

    [Theory]
    [MemberData(nameof(CriticalRegexes))]
    public void RegexPatternsCompile(Type declaringType, string fieldName, string sample)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var regex = Assert.IsType<Regex>(field!.GetValue(null));
        regex.IsMatch(sample);
        Assert.True(regex.Options.HasFlag(RegexOptions.Compiled));
        Assert.True(regex.Options.HasFlag(RegexOptions.CultureInvariant));
    }
}
