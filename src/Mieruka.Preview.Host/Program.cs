using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Preview;
using Mieruka.Preview.Contracts;
using Mieruka.Preview.Ipc;

namespace Mieruka.Preview.Host;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: Mieruka.Preview.Host <monitorId> [preferGpu:true|false] [pipeName]");
            return 1;
        }

        var monitorId = args[0];
        var preferGpu = args.Length > 1 && bool.TryParse(args[1], out var parsed) ? parsed : true;
        var pipeName = args.Length > 2 ? args[2] : $"mieruka_preview_{Environment.UserName}_{Environment.ProcessId}";

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var monitor = MonitorLocator.Find(monitorId);
        if (monitor is null)
        {
            Console.Error.WriteLine($"Monitor not found: {monitorId}");
            return 2;
        }

        var resolution = monitor.GetPreviewResolution();
        var server = new PreviewIpcServer(pipeName);
        Console.WriteLine($"Waiting for IPC client on {pipeName}...");
        await server.StartAsync(cts.Token).ConfigureAwait(false);

        await server.SendAsync(
            PreviewIpcMessageKind.Status,
            new PreviewStatusMessage(new PreviewBackendInfo(PreviewBackendKind.Gdi, PreviewSettings.Default.TargetFpsGdi),
                "Ready",
                $"Monitor={monitorId}"),
            cts.Token).ConfigureAwait(false);

        var runner = new CaptureRunner(server, monitorId, monitor, resolution, preferGpu);
        await runner.StartAsync(cts.Token).ConfigureAwait(false);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var message = await server.ReadAsync(cts.Token).ConfigureAwait(false);
                if (message is null)
                {
                    await Task.Delay(50, cts.Token).ConfigureAwait(false);
                    continue;
                }

                await using (message)
                {
                    if (message.Kind == PreviewIpcMessageKind.StopCommand)
                    {
                        cts.Cancel();
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await runner.StopAsync().ConfigureAwait(false);
        }

        return 0;
    }
}

internal sealed class CaptureRunner
{
    private readonly PreviewIpcServer _server;
    private readonly string _monitorId;
    private readonly MonitorInfo _monitor;
    private readonly PreviewResolution _resolution;
    private readonly bool _preferGpu;
    private IMonitorCapture? _capture;
    private PreviewBackendInfo? _backend;
    private TimeSpan _frameInterval;
    private long _frameIndex;
    private DateTime _nextFrameAtUtc;

    public CaptureRunner(PreviewIpcServer server, string monitorId, MonitorInfo monitor, PreviewResolution resolution, bool preferGpu)
    {
        _server = server;
        _monitorId = monitorId;
        _monitor = monitor;
        _resolution = resolution;
        _preferGpu = preferGpu;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _capture = await TryCreateCaptureAsync(_preferGpu, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start capture: {ex.Message}");
            throw;
        }

        if (_capture is null)
        {
            throw new InvalidOperationException("No capture backend available.");
        }

        _capture.FrameArrived += OnFrameArrived;
        await _capture.StartAsync(_monitor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IMonitorCapture> TryCreateCaptureAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        if (preferGpu)
        {
            try
            {
                var gpu = await CaptureFactory.GpuAsync(_monitorId, cancellationToken).ConfigureAwait(false);
                _backend = new PreviewBackendInfo(PreviewBackendKind.Gpu, PreviewSettings.Default.TargetFpsGpu, "GPU");
                _frameInterval = PreviewSettings.Default.GetGpuFrameInterval();
                return gpu;
            }
            catch
            {
                // fallback to GDI
            }
        }

        _backend = new PreviewBackendInfo(PreviewBackendKind.Gdi, PreviewSettings.Default.TargetFpsGdi, "GDI");
        _frameInterval = PreviewSettings.Default.GetGdiFrameInterval();
        return await CaptureFactory.GdiAsync(_monitorId).ConfigureAwait(false);
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        if (_backend is null)
        {
            e.Dispose();
            return;
        }

        try
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextFrameAtUtc)
            {
                return;
            }

            _nextFrameAtUtc = nowUtc + _frameInterval;
            using var processed = DownscaleFrame(e.Frame, _resolution);
            var bmpData = processed.LockBits(new Rectangle(0, 0, processed.Width, processed.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);

            try
            {
                var buffer = new byte[Math.Max(0, bmpData.Stride * bmpData.Height)];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, buffer.Length);

                var frame = new PreviewFrameMessage(
                    _backend,
                    processed.Width,
                    processed.Height,
                    bmpData.Stride,
                    bmpData.PixelFormat,
                    Interlocked.Increment(ref _frameIndex),
                    e.Timestamp,
                    buffer);

                _ = _server.SendAsync(PreviewIpcMessageKind.Frame, frame, buffer, CancellationToken.None);
            }
            finally
            {
                processed.UnlockBits(bmpData);
            }
        }
        finally
        {
            e.Dispose();
        }
    }

    private static Bitmap DownscaleFrame(Bitmap source, PreviewResolution resolution)
    {
        if (!resolution.HasValidSize || (source.Width == resolution.LogicalWidth && source.Height == resolution.LogicalHeight))
        {
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppPArgb);
        }

        var target = new Bitmap(resolution.LogicalWidth, resolution.LogicalHeight, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(target);
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        g.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height));
        return target;
    }

    public async Task StopAsync()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.FrameArrived -= OnFrameArrived;
        await _capture.StopAsync().ConfigureAwait(false);
        await _capture.DisposeAsync().ConfigureAwait(false);
        _capture = null;
    }
}
