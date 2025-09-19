using System.Windows.Forms;

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
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
