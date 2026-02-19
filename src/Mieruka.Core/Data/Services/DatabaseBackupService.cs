using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Provides backup and restore operations for the Mieruka SQLite database.
/// </summary>
public sealed class DatabaseBackupService
{
    private static readonly ILogger Logger = Log.ForContext<DatabaseBackupService>();

    private static string GetDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Mieruka", "mieruka.db");
    }

    /// <summary>
    /// Creates a backup of the database to the specified destination path.
    /// Performs a WAL checkpoint before copying to ensure all data is flushed.
    /// </summary>
    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourcePath = GetDatabasePath();
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Database file not found.", sourcePath);
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Flush WAL to main database file before copying
        await CheckpointAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite: true), cancellationToken).ConfigureAwait(false);

        Logger.Information("Database backed up to {DestinationPath}", destinationPath);
    }

    /// <summary>
    /// Restores the database from the specified source path.
    /// The application should be restarted after a restore operation.
    /// </summary>
    public async Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourcePath);
        }

        // Basic validation: check SQLite header
        var header = new byte[16];
        using (var fs = File.OpenRead(sourcePath))
        {
            var bytesRead = await fs.ReadAsync(header, cancellationToken).ConfigureAwait(false);
            if (bytesRead < 16 || header[0] != 0x53 || header[1] != 0x51 || header[2] != 0x4C)
            {
                throw new InvalidDataException("The file does not appear to be a valid SQLite database.");
            }
        }

        var databasePath = GetDatabasePath();
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Remove WAL and SHM files before restoring
        var walPath = databasePath + "-wal";
        var shmPath = databasePath + "-shm";

        await Task.Run(() =>
        {
            File.Copy(sourcePath, databasePath, overwrite: true);

            if (File.Exists(walPath))
            {
                try { File.Delete(walPath); } catch { /* best-effort */ }
            }

            if (File.Exists(shmPath))
            {
                try { File.Delete(shmPath); } catch { /* best-effort */ }
            }
        }, cancellationToken).ConfigureAwait(false);

        Logger.Information("Database restored from {SourcePath}", sourcePath);
    }

    private static async Task CheckpointAsync(string databasePath, CancellationToken cancellationToken)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        using var connection = new SqliteConnection(csb.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
