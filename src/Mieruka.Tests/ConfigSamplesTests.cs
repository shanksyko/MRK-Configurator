using System;
using System.IO;
using System.Text.Json;
using Mieruka.Core.Services;
using Xunit;

namespace Mieruka.Tests;

public sealed class ConfigSamplesTests
{
    [Fact]
    public void SampleConfig_DefinesCycleWithValidItems()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "appsettings.sample.json");
        Assert.True(File.Exists(path));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var general = document.RootElement.GetProperty("GeneralConfig");
        var cycle = general.GetProperty("Cycle");

        Assert.True(cycle.GetProperty("Enabled").GetBoolean());
        Assert.True(cycle.GetProperty("DefaultDurationSeconds").GetInt32() > 0);

        foreach (var item in cycle.GetProperty("Items").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("Id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("TargetId").GetString()));
            Assert.True(item.GetProperty("DurationSeconds").GetInt32() > 0);
            Assert.True(item.GetProperty("Enabled").GetBoolean());
        }
    }

    [Fact]
    public void SampleConfig_DpiSettings_AreConsistentWithDisplayUtils()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "appsettings.sample.json");
        Assert.True(File.Exists(path));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var monitors = document.RootElement.GetProperty("GeneralConfig").GetProperty("Monitors");

        foreach (var monitor in monitors.EnumerateArray())
        {
            var width = monitor.GetProperty("Width").GetDouble();
            var scale = monitor.GetProperty("Scale").GetDouble();

            var dips = DisplayUtils.PxToDip(width, scale);
            var pixels = DisplayUtils.DipToPx(dips, scale);

            Assert.Equal(width, pixels, 5);
        }
    }
}
