using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.App.Config;

/// <summary>
/// Persists configuration objects to disk as JSON files.
/// </summary>
/// <typeparam name="T">Type of the payload.</typeparam>
public sealed class JsonStore<T>
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly TimeSpan _lockRetryDelay;

    /// <summary>
    /// Gets the full path to the storage file.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStore{T}"/> class.
    /// </summary>
    /// <param name="filePath">Target file path.</param>
    /// <param name="serializerOptions">Optional serializer configuration.</param>
    /// <param name="lockRetryDelay">Optional delay used when waiting for the lock.</param>
    public JsonStore(string filePath, JsonSerializerOptions? serializerOptions = null, TimeSpan? lockRetryDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = Path.GetFullPath(filePath);
        _serializerOptions = serializerOptions ?? DefaultSerializerOptions;
        _lockRetryDelay = lockRetryDelay ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>
    /// Loads the payload from disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized payload or <c>null</c> when not present.</returns>
    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var fileLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);

        if (!File.Exists(_filePath))
        {
            return default;
        }

        await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a payload to disk atomically.
    /// </summary>
    /// <param name="value">Value that should be persisted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = _filePath + ".tmp";

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _serializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 120;
        var lockPath = _filePath + ".lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // On the last attempt, try to remove a potentially orphaned lock file.
                if (attempt == maxRetries - 1)
                {
                    TryRemoveOrphanedLock(lockPath);
                }

                await Task.Delay(_lockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Failed to acquire file lock for '{_filePath}' after {maxRetries} attempts.");
    }

    private static void TryRemoveOrphanedLock(string lockPath)
    {
        try
        {
            var info = new FileInfo(lockPath);
            if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds > 30)
            {
                info.Delete();
            }
        }
        catch (IOException)
        {
            // Another process may still hold the lock — ignore.
        }
    }
}
