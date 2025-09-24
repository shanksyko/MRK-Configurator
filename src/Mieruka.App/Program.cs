using System;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Forms;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var store = ConfigurationBootstrapper.CreateStore();
            _ = ConfigurationBootstrapper.LoadAsync(store).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize configuration: {ex.Message}",
                "MRK Configurator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm());
    }
}
