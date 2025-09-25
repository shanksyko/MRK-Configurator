using Mieruka.Core.Models;
using Xunit;

namespace Mieruka.Tests;

public sealed class MonitorIdentifierTests
{
    [Fact]
    public void Create_UsesAdapterInformationWhenPresent()
    {
        var key = new MonitorKey
        {
            AdapterLuidHigh = 0x1A2B3C4D,
            AdapterLuidLow = 0x55667788,
            TargetId = unchecked((int)0x90ABCDEF),
            DeviceId = "DISPLAY\\ABC123",
        };

        var identifier = MonitorIdentifier.Create(key, "\\\\.\\DISPLAY1");

        Assert.StartsWith("1A2B3C4D:55667788/90ABCDEF", identifier);
        Assert.EndsWith("|\\\\.\\DISPLAY1", identifier);
    }

    [Fact]
    public void TryParse_RoundtripsModernIdentifier()
    {
        var originalKey = new MonitorKey
        {
            AdapterLuidHigh = 0x01020304,
            AdapterLuidLow = unchecked((int)0xA0B0C0D0),
            TargetId = 0x11121314,
            DeviceId = "DISPLAY\\XYZ",
        };

        var identifier = MonitorIdentifier.Create(originalKey, "\\\\.\\DISPLAY2");
        var success = MonitorIdentifier.TryParse(identifier, out var parsedKey, out var deviceName);

        Assert.True(success);
        Assert.Equal(originalKey.AdapterLuidHigh, parsedKey.AdapterLuidHigh);
        Assert.Equal(originalKey.AdapterLuidLow, parsedKey.AdapterLuidLow);
        Assert.Equal(originalKey.TargetId, parsedKey.TargetId);
        Assert.Equal("\\\\.\\DISPLAY2", deviceName);
        Assert.Equal("\\\\.\\DISPLAY2", parsedKey.DeviceId);
    }

    [Fact]
    public void TryParse_SupportsLegacyIdentifiers()
    {
        const string identifier = "0ABCDEF0-12345678-1ABCDEF0-DISPLAY#10";

        var success = MonitorIdentifier.TryParse(identifier, out var key, out var deviceName);

        Assert.True(success);
        Assert.Equal(0x0ABCDEF0, key.AdapterLuidHigh);
        Assert.Equal(0x12345678, key.AdapterLuidLow);
        Assert.Equal(0x1ABCDEF0, key.TargetId);
        Assert.Equal("DISPLAY#10", deviceName);
        Assert.Equal("DISPLAY#10", key.DeviceId);
    }

    [Fact]
    public void TryParse_FallsBackToDeviceNameWhenNoAdapterInformation()
    {
        var success = MonitorIdentifier.TryParse("\\\\.\\DISPLAY3", out var key, out var deviceName);

        Assert.True(success);
        Assert.Equal("\\\\.\\DISPLAY3", deviceName);
        Assert.Equal("\\\\.\\DISPLAY3", key.DeviceId);
        Assert.Equal(0, key.AdapterLuidHigh);
        Assert.Equal(0, key.AdapterLuidLow);
        Assert.Equal(0, key.TargetId);
    }
}
