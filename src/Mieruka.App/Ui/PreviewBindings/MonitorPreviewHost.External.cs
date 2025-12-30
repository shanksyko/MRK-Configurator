using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Mieruka.Preview.Contracts;
using Mieruka.Preview.Ipc;

namespace Mieruka.App.Ui.PreviewBindings;

[SupportedOSPlatform("windows")]
public sealed partial class MonitorPreviewHost
{
    private PreviewHostSession? _externalSession;
    public bool UseExternalPreviewHost { get; set; } = true;

    private async Task<bool> TryStartExternalAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        if (_externalSession is not null)
        {
            return true;
        }

        var hostPath = LocateHostBinary();
        if (hostPath is null)
        {
            return false;
        }

        var pipeName = $"mieruka_preview_{Environment.UserName}_{MonitorId.Replace(':', '_').Replace('\\', '_')}";
        var session = new PreviewHostSession(hostPath, pipeName, MonitorId, preferGpu, cancellationToken);
        session.FrameReceived += OnExternalFrame;

        try
        {
            await session.StartAsync().ConfigureAwait(false);
            _externalSession = session;
            lock (_stateGate)
            {
                _hasActiveSession = true;
                _isGpuActive = session.Backend?.Backend == PreviewBackendKind.Gpu;
                _isSuspended = false;
            }

            return true;
        }
        catch
        {
            session.FrameReceived -= OnExternalFrame;
            await session.DisposeAsync().ConfigureAwait(false);
            return false;
        }
    }

    private async Task StopExternalAsync()
    {
        var session = _externalSession;
        _externalSession = null;
        if (session is null)
        {
            return;
        }

        session.FrameReceived -= OnExternalFrame;
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private void OnExternalFrame(Bitmap bitmap, DateTimeOffset timestamp)
    {
        var args = new Mieruka.Preview.MonitorFrameArrivedEventArgs(bitmap, timestamp);
        OnFrameArrived(this, args);
    }

    private static string? LocateHostBinary()
    {
        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var directory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var candidate = Path.Combine(directory, "Mieruka.Preview.Host.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class PreviewHostSession : IAsyncDisposable
{
    private readonly string _hostPath;
    private readonly string _pipeName;
    private readonly string _monitorId;
    private readonly bool _preferGpu;
    private readonly CancellationToken _cancellationToken;
    private PreviewIpcClient? _client;
    private Process? _process;
    private Task? _readLoop;

    public PreviewHostSession(string hostPath, string pipeName, string monitorId, bool preferGpu, CancellationToken cancellationToken)
    {
        _hostPath = hostPath;
        _pipeName = pipeName;
        _monitorId = monitorId;
        _preferGpu = preferGpu;
        _cancellationToken = cancellationToken;
    }

    public PreviewBackendInfo? Backend { get; private set; }

    public event Action<Bitmap, DateTimeOffset>? FrameReceived;

    public async Task StartAsync()
    {
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = _hostPath,
            Arguments = $"\"{_monitorId}\" {_preferGpu} {_pipeName}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        _client = new PreviewIpcClient(_pipeName);
        await _client.ConnectAsync(_cancellationToken).ConfigureAwait(false);

        _readLoop = Task.Run(ReadLoopAsync, _cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                var envelope = await _client.ReadAsync(_cancellationToken).ConfigureAwait(false);
                if (envelope is null)
                {
                    await Task.Delay(25, _cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    switch (envelope.Kind)
                    {
                        case PreviewIpcMessageKind.Frame:
                            var frame = envelope.Deserialize<PreviewFrameMessage>();
                            Backend ??= frame.Backend;
                            DispatchFrame(frame);
                            break;
                        case PreviewIpcMessageKind.Status:
                            Backend ??= envelope.Deserialize<PreviewStatusMessage>().Backend;
                            break;
                    }
                }
                finally
                {
                    await envelope.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DispatchFrame(PreviewFrameMessage frame)
    {
        if (frame.Buffer.Length < frame.ExpectedLength)
        {
            return;
        }

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(frame.Buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(frame.Width, frame.Height, frame.Stride, frame.PixelFormat, handle.AddrOfPinnedObject());
            var managedCopy = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(managedCopy))
            {
                g.DrawImage(bitmap, new Rectangle(0, 0, managedCopy.Width, managedCopy.Height));
            }

            FrameReceived?.Invoke(managedCopy, frame.Timestamp);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try
            {
                await _client.SendAsync(PreviewIpcMessageKind.StopCommand, new PreviewStopCommand(), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }
}
