using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Preview.Contracts;

namespace Mieruka.Preview.Ipc;

[SupportedOSPlatform("windows")]
public sealed class PreviewIpcClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private PreviewIpcChannel? _channel;

    public PreviewIpcClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            return;
        }

        _channel = await PreviewIpcChannel.ConnectNamedPipeAsync(_pipeName, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync<T>(PreviewIpcMessageKind kind, T payload, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("IPC channel not connected.");
        }

        await _channel.WriteAsync(kind, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreviewIpcEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return null;
        }

        return await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
