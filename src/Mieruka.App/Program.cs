using System;
using System.Threading;
using System.Windows.Forms;
using Mieruka.App.Forms;
using Mieruka.Core.Infra;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        Application.Run(new MainForm());
    }

    private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        if (e.Exception is not null)
        {
            Logger.Error("Exceção não tratada no thread da interface.", e.Exception);
        }

        MessageBox.Show(
            "Ocorreu um erro inesperado. O problema foi registrado no log em %LOCALAPPDATA%.",
            "Erro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Error("Exceção não tratada no domínio de aplicativo.", exception);
        }
        else
        {
            Logger.Error($"Exceção não tratada no domínio de aplicativo: {e.ExceptionObject}");
        }

        MessageBox.Show(
            "Ocorreu um erro crítico e o aplicativo precisa ser fechado. Consulte o log em %LOCALAPPDATA%.",
            "Erro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
