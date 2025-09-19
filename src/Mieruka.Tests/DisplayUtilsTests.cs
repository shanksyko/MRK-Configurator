using System;
using System.Drawing;
using Mieruka.Core.Services;
using Xunit;

namespace Mieruka.Tests;

public sealed class DisplayUtilsTests
{
    [Theory]
    [InlineData(1920, 1.0, 1920)]
    [InlineData(1920, 1.25, 1536)]
    [InlineData(1920, 1.5, 1280)]
    public void PxToDip_ReturnsExpectedValue(double pixels, double scale, double expected)
    {
        var result = DisplayUtils.PxToDip(pixels, scale);

        Assert.Equal(expected, result, 5);
    }

    [Theory]
    [InlineData(960, 1.0, 960)]
    [InlineData(960, 1.25, 1200)]
    [InlineData(960, 1.5, 1440)]
    public void DipToPx_ReturnsExpectedValue(double dips, double scale, double expected)
    {
        var result = DisplayUtils.DipToPx(dips, scale);

        Assert.Equal(expected, result, 5);
    }

    [Fact]
    public void Conversion_IsInvertible()
    {
        const double pixels = 800;
        const double dips = 600;

        Assert.Equal(pixels, DisplayUtils.DipToPx(DisplayUtils.PxToDip(pixels, 1.25), 1.25), 5);
        Assert.Equal(dips, DisplayUtils.PxToDip(DisplayUtils.DipToPx(dips, 1.5), 1.5), 5);
    }

    [Fact]
    public void PxToDip_ThrowsWhenScaleIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayUtils.PxToDip(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayUtils.PxToDip(10, -0.1));
    }

    [Fact]
    public void DipToPx_ThrowsWhenScaleIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayUtils.DipToPx(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayUtils.DipToPx(10, -0.1));
    }

    [Fact]
    public void ClampToWorkArea_ReturnsOriginalWhenAlreadyInside()
    {
        var workArea = new Rectangle(0, 0, 1920, 1080);
        var window = new Rectangle(100, 100, 800, 600);

        var result = DisplayUtils.ClampToWorkArea(window, workArea);

        Assert.Equal(window, result);
    }

    [Fact]
    public void ClampToWorkArea_RepositionsWindowThatExceedsWorkArea()
    {
        var workArea = new Rectangle(0, 0, 1920, 1080);
        var window = new Rectangle(1800, 900, 400, 400);

        var result = DisplayUtils.ClampToWorkArea(window, workArea);

        Assert.Equal(new Rectangle(1520, 680, 400, 400), result);
    }

    [Fact]
    public void ClampToWorkArea_ResizesWindowLargerThanWorkArea()
    {
        var workArea = new Rectangle(100, 50, 1280, 720);
        var window = new Rectangle(80, 20, 1600, 900);

        var result = DisplayUtils.ClampToWorkArea(window, workArea);

        Assert.Equal(new Rectangle(100, 50, 1280, 720), result);
    }
}
