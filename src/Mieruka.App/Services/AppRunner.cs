using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using static Serilog.Log;
using Drawing = System.Drawing;

namespace Mieruka.App.Services;

public interface IAppRunner
{
    event EventHandler? BeforeMoveWindow;

    event EventHandler? AfterMoveWindow;

    Task RunAndPositionAsync(
        AppConfig app,
        MonitorInfo monitor,
        Drawing.Rectangle bounds,
        CancellationToken cancellationToken = default);
}

public sealed class AppRunner : IAppRunner
{
    private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(5);

    public event EventHandler? BeforeMoveWindow;
    public event EventHandler? AfterMoveWindow;

    public async Task RunAndPositionAsync(
        AppConfig app,
        MonitorInfo monitor,
        Drawing.Rectangle bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(monitor);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Executar aplicativos reais está disponível apenas no Windows.");
        }

        var executablePath = app.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Nenhum executável foi informado para o teste.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Executável não encontrado.", executablePath);
        }

        var arguments = string.IsNullOrWhiteSpace(app.Arguments) ? null : app.Arguments;
        var alwaysOnTop = app.Window.AlwaysOnTop;

        try
        {
            OnBeforeMoveWindow();

            var existingProcess = FindRunningProcess(executablePath);
            if (existingProcess is not null)
            {
                try
                {
                    await PositionExistingProcessAsync(existingProcess, monitor, bounds, alwaysOnTop, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    existingProcess.Dispose();
                }

                return;
            }

            await LaunchAndPositionProcessAsync(executablePath, arguments, monitor, bounds, alwaysOnTop, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "RunAndPositionAsync falhou para {AppId}", app.Name ?? app.Id);
            throw;
        }
        finally
        {
            OnAfterMoveWindow();
        }
    }

    private async Task LaunchAndPositionProcessAsync(
        string executablePath,
        string? arguments,
        MonitorInfo monitor,
        Drawing.Rectangle bounds,
        bool alwaysOnTop,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Não foi possível iniciar o processo para teste.");
        }

        var handle = await WindowWaiter
            .WaitForMainWindowAsync(process, WindowWaitTimeout, cancellationToken)
            .ConfigureAwait(false);

        ApplyWindowPosition(handle, monitor, bounds, alwaysOnTop);
    }

    private async Task PositionExistingProcessAsync(
        Process process,
        MonitorInfo monitor,
        Drawing.Rectangle bounds,
        bool alwaysOnTop,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException("O processo selecionado já foi encerrado.");
        }

        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            handle = await WindowWaiter
                .WaitForMainWindowAsync(process, WindowWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyWindowPosition(handle, monitor, bounds, alwaysOnTop);
    }

    private void ApplyWindowPosition(IntPtr handle, MonitorInfo monitor, Drawing.Rectangle bounds, bool alwaysOnTop)
    {
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("A janela de destino não foi localizada.");
        }

        WindowMover.MoveTo(handle, monitor, bounds, alwaysOnTop, WindowMoveMode.Absolute, relativeToMonitor: false, restoreIfMinimized: true);
        User32.SetForegroundWindow(handle);
    }

    private void OnBeforeMoveWindow()
    {
        try
        {
            BeforeMoveWindow?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Subscriber exception in BeforeMoveWindow.");
        }
    }

    private void OnAfterMoveWindow()
    {
        try
        {
            AfterMoveWindow?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Subscriber exception in AfterMoveWindow.");
        }
    }

    private static Process? FindRunningProcess(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(executablePath);
            var processName = Path.GetFileNameWithoutExtension(normalized);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            var candidates = Process.GetProcessesByName(processName);
            Process? match = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    var module = candidate.MainModule;
                    var candidatePath = module?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidatePath) &&
                        string.Equals(Path.GetFullPath(candidatePath), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        match = candidate;
                        break;
                    }
                }
                catch (Win32Exception)
                {
                    // Ignore processes without access rights.
                }
                catch (InvalidOperationException)
                {
                    // Ignore processes that exited while enumerating.
                }
            }

            foreach (var candidate in candidates)
            {
                if (!ReferenceEquals(candidate, match))
                {
                    candidate.Dispose();
                }
            }

            if (match is not null)
            {
                return match;
            }
        }
        catch (Exception ex)
        {
            Logger.Information(ex, "Unexpected error while searching for running process.");
        }

        return null;
    }
}
