using System;
using System.Windows.Forms;
using Mieruka.App.Forms;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
