using System;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Preview.Contracts;

namespace Mieruka.Preview.Ipc;

[SupportedOSPlatform("windows")]
public sealed class PreviewIpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _server;
    private PreviewIpcChannel? _channel;

    public PreviewIpcServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            return;
        }

        _server = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await _server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = new PreviewIpcChannel(_server, ownsStream: false);
    }

    public async Task<PreviewIpcEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return null;
        }

        return await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync<T>(PreviewIpcMessageKind kind, T payload, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        await _channel.WriteAsync(kind, payload, buffer, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync<T>(PreviewIpcMessageKind kind, T payload, CancellationToken cancellationToken)
    {
        await SendAsync(kind, payload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        _server?.Dispose();
    }
}
