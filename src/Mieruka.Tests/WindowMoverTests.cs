using System;
using System.Drawing;
using Mieruka.Core.Interop;
using Xunit;

namespace Mieruka.Tests;

public sealed class WindowMoverTests
{
    [Fact]
    public void MoveTo_ThrowsOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(
            () => WindowMover.MoveTo(new IntPtr(1), new Rectangle(0, 0, 100, 100), topMost: false, restoreIfMinimized: false));
    }

    [Fact]
    public void MoveTo_ThrowsWhenHandleIsZero()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(
            () => WindowMover.MoveTo(IntPtr.Zero, new Rectangle(0, 0, 100, 100), topMost: false, restoreIfMinimized: false));
    }

    [Fact]
    public void MoveTo_ThrowsWhenHandleIsInvalid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var invalidHandle = new IntPtr(123456);
        Assert.Throws<ArgumentException>(
            () => WindowMover.MoveTo(invalidHandle, new Rectangle(0, 0, 100, 100), topMost: false, restoreIfMinimized: false));
    }
}
