using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Preview.Contracts;

namespace Mieruka.Preview.Ipc;

public sealed class PreviewIpcChannel : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public PreviewIpcChannel(Stream stream, bool ownsStream = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
    }

    public static async Task<PreviewIpcChannel> ConnectNamedPipeAsync(string pipeName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return new PreviewIpcChannel(client, ownsStream: true);
    }

    public async Task WriteAsync<T>(PreviewIpcMessageKind kind, T payload, ReadOnlyMemory<byte> binaryPayload, CancellationToken cancellationToken)
    {
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _serializerOptions);
        var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((int)kind);
        writer.Write(headerBytes.Length);
        writer.Write(binaryPayload.Length);
        writer.Flush();
        await _stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (!binaryPayload.IsEmpty)
        {
            await _stream.WriteAsync(binaryPayload, cancellationToken).ConfigureAwait(false);
        }

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync<T>(PreviewIpcMessageKind kind, T payload, CancellationToken cancellationToken)
    {
        await WriteAsync(kind, payload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreviewIpcEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        var reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
        var typeRaw = await ReadInt32Async(reader, cancellationToken).ConfigureAwait(false);
        if (typeRaw is null)
        {
            return null;
        }

        var kind = (PreviewIpcMessageKind)typeRaw.Value;
        var headerLength = await ReadInt32Async(reader, cancellationToken).ConfigureAwait(false);
        var payloadLength = await ReadInt32Async(reader, cancellationToken).ConfigureAwait(false);

        if (headerLength is null || payloadLength is null)
        {
            return null;
        }

        var headerBuffer = ArrayPool<byte>.Shared.Rent(headerLength.Value);
        try
        {
            var headerMemory = new Memory<byte>(headerBuffer, 0, headerLength.Value);
            if (!await ReadExactlyAsync(headerMemory, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            byte[]? payloadBuffer = null;
            if (payloadLength.Value > 0)
            {
                payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength.Value);
                if (!await ReadExactlyAsync(new Memory<byte>(payloadBuffer, 0, payloadLength.Value), cancellationToken).ConfigureAwait(false))
                {
                    ArrayPool<byte>.Shared.Return(payloadBuffer);
                    return null;
                }
            }

            return new PreviewIpcEnvelope(kind, headerMemory[..headerLength.Value].ToArray(), payloadBuffer, payloadLength.Value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    private static async Task<int?> ReadInt32Async(BinaryReader reader, CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(int)];
        var read = await reader.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < sizeof(int))
        {
            return null;
        }

        return BitConverter.ToInt32(buffer, 0);
    }

    private async Task<bool> ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var remaining = buffer.Length;
        var offset = 0;
        while (remaining > 0)
        {
            var read = await _stream.ReadAsync(buffer.Slice(offset, remaining), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
            remaining -= read;
        }

        return true;
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsStream)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Represents an IPC message envelope containing header and optional payload.
/// This class must be disposed after use to return rented buffers to the ArrayPool.
/// Ownership: The caller of ReadAsync owns the envelope and must dispose it.
/// </summary>
public sealed class PreviewIpcEnvelope : IAsyncDisposable, IDisposable
{
    private int _disposed;

    public PreviewIpcEnvelope(PreviewIpcMessageKind kind, byte[] header, byte[]? payloadBuffer, int payloadLength)
    {
        Kind = kind;
        Header = header ?? throw new ArgumentNullException(nameof(header));
        PayloadBuffer = payloadBuffer;
        PayloadLength = payloadLength;
    }

    public PreviewIpcMessageKind Kind { get; }
    public byte[] Header { get; }
    public byte[]? PayloadBuffer { get; }
    public int PayloadLength { get; }

    public T Deserialize<T>(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(Header, options ?? new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        })!;
    }

    public ReadOnlyMemory<byte> GetPayload() => PayloadBuffer is null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(PayloadBuffer, 0, PayloadLength);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (PayloadBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(PayloadBuffer);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
