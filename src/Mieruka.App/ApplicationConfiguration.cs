using WinForms = System.Windows.Forms;

namespace Mieruka.App;

/// <summary>
/// Provides common application-wide configuration for WinForms.
/// </summary>
internal static class ApplicationConfiguration
{
    /// <summary>
    /// Initializes high DPI settings and visual styles.
    /// </summary>
    public static void Initialize()
    {
        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetCompatibleTextRenderingDefault(false);
    }
}
