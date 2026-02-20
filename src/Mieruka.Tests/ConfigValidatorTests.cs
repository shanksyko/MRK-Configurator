using System;
using System.Collections.Generic;
using System.Linq;
using Mieruka.App.Config;
using Mieruka.Core.Models;
using Xunit;

namespace Mieruka.Tests;

public sealed class ConfigValidatorTests
{
    private readonly ConfigValidator _validator = new(baseDirectory: AppContext.BaseDirectory);

    private static GeneralConfig EmptyConfig() => new();

    private static GeneralConfig ConfigWithSite(string id = "site1", string url = "https://example.com") =>
        new()
        {
            Sites = new List<SiteConfig>
            {
                new() { Id = id, Url = url }
            }
        };

    private static GeneralConfig ConfigWithApp(string id = "app1", string exe = "notepad.exe") =>
        new()
        {
            Applications = new List<AppConfig>
            {
                new() { Id = id, ExecutablePath = exe }
            }
        };

    // ───── Empty / default ─────

    [Fact]
    public void Validate_EmptyConfig_ReturnsReport()
    {
        var report = _validator.Validate(EmptyConfig(), null);

        Assert.NotNull(report);
        Assert.NotNull(report.Issues);
    }

    [Fact]
    public void Validate_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, null));
    }

    // ───── Sites ─────

    [Fact]
    public void Validate_SiteWithoutId_ReportsError()
    {
        var config = ConfigWithSite(id: "", url: "https://example.com");

        var report = _validator.Validate(config, null);

        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, i =>
            i.Severity == ConfigValidationSeverity.Error &&
            i.Message.Contains("identificador", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SiteWithoutUrl_ReportsError()
    {
        var config = ConfigWithSite(id: "site1", url: "");

        var report = _validator.Validate(config, null);

        Assert.True(report.HasErrors);
    }

    [Fact]
    public void Validate_ValidSite_NoIdOrUrlErrors()
    {
        var config = ConfigWithSite();

        var report = _validator.Validate(config, null);

        // Exclude monitor-assignment issues; only check for ID/URL errors
        var siteConfigErrors = report.Issues.Where(i =>
            i.Severity == ConfigValidationSeverity.Error &&
            i.Source?.Contains("site", StringComparison.OrdinalIgnoreCase) == true &&
            !i.Message.Contains("monitor", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Empty(siteConfigErrors);
    }

    // ───── Applications ─────

    [Fact]
    public void Validate_AppWithoutId_ReportsError()
    {
        var config = ConfigWithApp(id: "");

        var report = _validator.Validate(config, null);

        Assert.True(report.HasErrors);
    }

    [Fact]
    public void Validate_AppWithoutExecutable_ReportsError()
    {
        var config = ConfigWithApp(id: "app1", exe: "");

        var report = _validator.Validate(config, null);

        Assert.True(report.HasErrors);
    }

    // ───── Cycle ─────

    [Fact]
    public void Validate_CycleReferencingMissingTarget_ReportsError()
    {
        var config = new GeneralConfig
        {
            Cycle = new CycleConfig
            {
                Enabled = true,
                Items = new List<CycleItem>
                {
                    new() { TargetType = "site", TargetId = "nonexistent" }
                }
            }
        };

        var report = _validator.Validate(config, null);

        Assert.True(report.HasErrors || report.HasWarnings);
    }

    [Fact]
    public void Validate_CycleWithValidTargets_NoIssues()
    {
        var config = new GeneralConfig
        {
            Sites = new List<SiteConfig>
            {
                new() { Id = "dashboard", Url = "https://example.com" }
            },
            Cycle = new CycleConfig
            {
                Enabled = true,
                Items = new List<CycleItem>
                {
                    new() { TargetType = "site", TargetId = "dashboard", DurationSeconds = 60 }
                }
            }
        };

        var report = _validator.Validate(config, null);

        var cycleErrors = report.Issues.Where(i =>
            i.Severity == ConfigValidationSeverity.Error &&
            i.Message.Contains("ciclo", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Empty(cycleErrors);
    }

    // ───── Report structure ─────

    [Fact]
    public void ValidationReport_Empty_HasNoIssues()
    {
        var report = ConfigValidationReport.Empty;

        Assert.False(report.HasErrors);
        Assert.False(report.HasWarnings);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
    }

    [Fact]
    public void ValidationReport_CountsErrorsAndWarnings()
    {
        var issues = new List<ConfigValidationIssue>
        {
            new(ConfigValidationSeverity.Error, "err1"),
            new(ConfigValidationSeverity.Error, "err2"),
            new(ConfigValidationSeverity.Warning, "warn1"),
        };

        var report = new ConfigValidationReport(issues);

        Assert.Equal(2, report.ErrorCount);
        Assert.Equal(1, report.WarningCount);
        Assert.True(report.HasErrors);
        Assert.True(report.HasWarnings);
    }

    // ───── Duplicate IDs ─────

    [Fact]
    public void Validate_DuplicateSiteIds_ReportsIssue()
    {
        var config = new GeneralConfig
        {
            Sites = new List<SiteConfig>
            {
                new() { Id = "dup", Url = "https://a.com" },
                new() { Id = "dup", Url = "https://b.com" },
            }
        };

        var report = _validator.Validate(config, null);

        Assert.True(report.Issues.Count > 0);
    }
}
