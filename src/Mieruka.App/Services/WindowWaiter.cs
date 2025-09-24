using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.App.Services;

internal static class WindowWaiter
{
    public static async Task<IntPtr> WaitForMainWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Aguardando janelas está disponível apenas no Windows.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException("O processo foi finalizado antes que a janela estivesse disponível.");
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);

            process.Refresh();
            handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        throw new TimeoutException("A janela principal não foi localizada dentro do tempo limite.");
    }
}
