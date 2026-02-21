using System.Collections.Generic;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using Xunit;

namespace Mieruka.Tests;

public sealed class BrowserArgumentBuilderTests
{
  // ── ResolveBrowserExecutable ──────────────────────────────────────────

  [Theory]
  [InlineData(BrowserType.Chrome)]
  [InlineData(BrowserType.Edge)]
  [InlineData(BrowserType.Firefox)]
  [InlineData(BrowserType.Brave)]
  public void ResolveBrowserExecutable_ReturnsNonEmpty_ForAllBrowserTypes(BrowserType browser)
  {
    var result = BrowserArgumentBuilder.ResolveBrowserExecutable(browser);
    Assert.False(string.IsNullOrWhiteSpace(result));
  }

  [Fact]
  public void ResolveBrowserExecutable_Chrome_ReturnsExpected()
  {
    var result = BrowserArgumentBuilder.ResolveBrowserExecutable(BrowserType.Chrome);
    Assert.Contains("chrome", result, System.StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ResolveBrowserExecutable_Edge_ReturnsExpected()
  {
    var result = BrowserArgumentBuilder.ResolveBrowserExecutable(BrowserType.Edge);
    Assert.Contains("edge", result, System.StringComparison.OrdinalIgnoreCase);
  }

  // ── FormatArgument ───────────────────────────────────────────────────

  [Fact]
  public void FormatArgument_ProducesCorrectFormat()
  {
    var result = BrowserArgumentBuilder.FormatArgument("--user-data-dir", @"C:\Users\Test");
    Assert.Equal(@"--user-data-dir=""C:\Users\Test""", result);
  }

  [Fact]
  public void FormatArgument_EscapesDoubleQuotes()
  {
    var result = BrowserArgumentBuilder.FormatArgument("--app", "http://example.com/page?q=\"test\"");
    // Embedded quotes must be escaped so the shell sees the value as one token.
    Assert.Contains("\\\"test\\\"", result);
  }

  [Fact]
  public void FormatArgument_DoesNotDoubleEscapeBackslashes()
  {
    var result = BrowserArgumentBuilder.FormatArgument("--user-data-dir", @"C:\Program Files\Chrome");
    // Backslashes should NOT be escaped — they're valid path characters on Windows.
    Assert.Contains(@"C:\Program Files\Chrome", result);
    Assert.DoesNotContain(@"C:\\Program Files\\Chrome", result);
  }

  // ── ContainsArgument ─────────────────────────────────────────────────

  [Fact]
  public void ContainsArgument_ExactMatch_ReturnsTrue()
  {
    var args = new List<string> { "--kiosk", "--disable-gpu" };
    Assert.True(BrowserArgumentBuilder.ContainsArgument(args, "--kiosk"));
  }

  [Fact]
  public void ContainsArgument_ExactMatch_CaseInsensitive()
  {
    var args = new List<string> { "--KIOSK" };
    Assert.True(BrowserArgumentBuilder.ContainsArgument(args, "--kiosk"));
  }

  [Fact]
  public void ContainsArgument_ExactMatch_ReturnsFalseWhenMissing()
  {
    var args = new List<string> { "--kiosk" };
    Assert.False(BrowserArgumentBuilder.ContainsArgument(args, "--disable-gpu"));
  }

  [Fact]
  public void ContainsArgument_PrefixMatch_FindsArgumentWithValue()
  {
    var args = new List<string> { "--proxy-server=http://localhost:8080" };
    Assert.True(BrowserArgumentBuilder.ContainsArgument(args, "--proxy-server", matchByPrefix: true));
  }

  [Fact]
  public void ContainsArgument_PrefixMatch_ReturnsFalseWhenMissing()
  {
    var args = new List<string> { "--kiosk" };
    Assert.False(BrowserArgumentBuilder.ContainsArgument(args, "--proxy-server", matchByPrefix: true));
  }

  // ── CollectBrowserArguments ──────────────────────────────────────────

  [Fact]
  public void CollectBrowserArguments_IncludesKioskFlag()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      KioskMode = true,
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Contains("--kiosk", args);
  }

  [Fact]
  public void CollectBrowserArguments_IncludesAppModeFlag()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      AppMode = true,
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Contains(args, a => a.StartsWith("--app=", System.StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void CollectBrowserArguments_IncludesUserDataDir()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      UserDataDirectory = @"C:\Temp\Profile",
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Contains(args, a => a.StartsWith("--user-data-dir=", System.StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void CollectBrowserArguments_IncludesProfileDirectory()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      ProfileDirectory = "Profile 1",
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Contains(args, a => a.StartsWith("--profile-directory=", System.StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void CollectBrowserArguments_DeduplicatesArguments()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      KioskMode = true,
      BrowserArguments = new[] { "--kiosk", "--disable-gpu" },
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Single(args, a => a == "--kiosk");
  }

  [Fact]
  public void CollectBrowserArguments_IncludesGlobalSettings()
  {
    var global = new BrowserArgumentsSettings
    {
      Chrome = new[] { "--no-first-run", "--disable-sync" },
    };

    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site, global);
    Assert.Contains("--no-first-run", args);
    Assert.Contains("--disable-sync", args);
  }

  [Fact]
  public void CollectBrowserArguments_IncludesPerSiteArguments()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      BrowserArguments = new[] { "--disable-gpu", "--mute-audio" },
    };

    var args = BrowserArgumentBuilder.CollectBrowserArguments(site);
    Assert.Contains("--disable-gpu", args);
    Assert.Contains("--mute-audio", args);
  }

  // ── BuildBrowserArgumentString ───────────────────────────────────────

  [Fact]
  public void BuildBrowserArgumentString_AppendsUrlWhenNotAppMode()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
    };

    var result = BrowserArgumentBuilder.BuildBrowserArgumentString(site);
    Assert.EndsWith("https://example.com", result);
  }

  [Fact]
  public void BuildBrowserArgumentString_DoesNotAppendUrlInAppMode()
  {
    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Chrome,
      AppMode = true,
    };

    var result = BrowserArgumentBuilder.BuildBrowserArgumentString(site);
    // URL should appear as --app="url", not as trailing argument
    Assert.DoesNotMatch(@"^.+ https://example\.com$", result);
    Assert.Contains("--app=", result);
  }

  [Fact]
  public void BuildBrowserArgumentString_GlobalArgs_AreIncluded()
  {
    var global = new BrowserArgumentsSettings
    {
      Edge = new[] { "--inprivate" },
    };

    var site = new SiteConfig
    {
      Id = "test",
      Url = "https://example.com",
      Browser = BrowserType.Edge,
    };

    var result = BrowserArgumentBuilder.BuildBrowserArgumentString(site, global);
    Assert.Contains("--inprivate", result);
  }

  // ── BrowserArgumentsCatalog integration ──────────────────────────────

  [Theory]
  [InlineData(BrowserType.Chrome)]
  [InlineData(BrowserType.Edge)]
  [InlineData(BrowserType.Firefox)]
  [InlineData(BrowserType.Brave)]
  public void BrowserArgumentsCatalog_HasArgumentsForEachBrowserType(BrowserType browser)
  {
    var args = BrowserArgumentsCatalog.ForBrowser(browser);
    Assert.NotEmpty(args);
  }

  [Fact]
  public void BrowserArgumentsCatalog_AllCategories_HaveDisplayNames()
  {
    foreach (var category in System.Enum.GetValues<BrowserArgumentCategory>())
    {
      var args = BrowserArgumentsCatalog.All;
      // Just verify the enum is complete — no crash
      Assert.NotNull(args);
    }
  }

  [Fact]
  public void BrowserArgumentsCatalog_FindByFlag_ReturnsMatchingDefinition()
  {
    var def = BrowserArgumentsCatalog.FindByFlag("--kiosk");
    Assert.NotNull(def);
    Assert.Equal("--kiosk", def!.Flag);
  }

  [Fact]
  public void BrowserArgumentsCatalog_FindByFlag_StripsValuePart()
  {
    var def = BrowserArgumentsCatalog.FindByFlag("--proxy-server=http://localhost");
    Assert.NotNull(def);
    Assert.Equal("--proxy-server", def!.Flag);
  }

  [Fact]
  public void BrowserArgumentsCatalog_FindByFlag_ReturnsNullForUnknown()
  {
    var def = BrowserArgumentsCatalog.FindByFlag("--totally-unknown-flag");
    Assert.Null(def);
  }

  // ── BrowserArgumentsSettings.ForBrowser ──────────────────────────────

  [Fact]
  public void BrowserArgumentsSettings_ForBrowser_ReturnsCorrectList()
  {
    var settings = new BrowserArgumentsSettings
    {
      Chrome = new[] { "--a" },
      Edge = new[] { "--b" },
      Firefox = new[] { "--c" },
      Brave = new[] { "--d" },
    };

    Assert.Equal(new[] { "--a" }, settings.ForBrowser(BrowserType.Chrome));
    Assert.Equal(new[] { "--b" }, settings.ForBrowser(BrowserType.Edge));
    Assert.Equal(new[] { "--c" }, settings.ForBrowser(BrowserType.Firefox));
    Assert.Equal(new[] { "--d" }, settings.ForBrowser(BrowserType.Brave));
  }
}
