#nullable enable
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Ui;

internal static class ScreenExtensions
{
    public static string FriendlyName(this WinForms.Screen screen)
    {
        return string.IsNullOrWhiteSpace(screen.DeviceName)
            ? "Monitor"
            : screen.DeviceName;
    }
}
