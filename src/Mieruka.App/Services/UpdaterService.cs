using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using Serilog;

namespace Mieruka.App.Services;

/// <summary>
/// Periodically checks for application updates and applies them when available.
/// </summary>
public sealed class UpdaterService : IDisposable
{
    private const int MinimumIntervalMinutes = 5;
    private static readonly HttpClient SharedHttpClient = new();

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITelemetry _telemetry;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _applicationDirectory;
    private readonly object _gate = new();

    // Usa System.Threading.Timer (thread pool). Não tocar UI diretamente; marshalar para o thread da UI com BeginInvoke.
    private System.Threading.Timer? _threadTimer;
    private UpdateConfig _configuration = new();
    private Version _currentVersion;
    private int _updateInProgress;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdaterService"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to report update activity.</param>
    /// <param name="httpClient">Optional HTTP client used for remote requests.</param>
    /// <param name="applicationDirectory">Optional directory that should receive the updated binaries.</param>
    public UpdaterService(ITelemetry? telemetry = null, HttpClient? httpClient = null, string? applicationDirectory = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;
        _httpClient = httpClient ?? SharedHttpClient;
        _ownsHttpClient = false;
        _applicationDirectory = ResolveApplicationDirectory(applicationDirectory);
        _currentVersion = ResolveCurrentVersion();
    }

    /// <summary>
    /// Applies the provided update configuration.
    /// </summary>
    /// <param name="config">Configuration describing the update endpoint and schedule.</param>
    public void ApplyConfiguration(UpdateConfig? config)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            _configuration = config ?? new UpdateConfig();

            _threadTimer?.Dispose();
            _threadTimer = null;

            if (!_configuration.Enabled || string.IsNullOrWhiteSpace(_configuration.ManifestUrl))
            {
                return;
            }

            var interval = ResolveInterval(_configuration.CheckIntervalMinutes);
            _threadTimer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, interval);
        }
    }

    /// <summary>
    /// Releases resources used by the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _threadTimer?.Dispose();
            _threadTimer = null;
            _disposed = true;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void OnTimerTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _updateInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckForUpdatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _telemetry.Warn("Falha inesperada durante a verificação de atualizações.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _updateInProgress, 0);
            }
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateConfig config;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            config = _configuration;
        }

        if (!config.Enabled || string.IsNullOrWhiteSpace(config.ManifestUrl))
        {
            return;
        }

        var manifest = await FetchManifestAsync(config.ManifestUrl!).ConfigureAwait(false);
        if (manifest is null)
        {
            _telemetry.Warn("Manifesto de atualização não pôde ser interpretado.");
            return;
        }

        if (!TryParseVersion(manifest.Version, out var remoteVersion))
        {
            _telemetry.Warn("Manifesto de atualização inválido: versão ausente ou com formato incorreto.");
            return;
        }

        var currentVersion = Volatile.Read(ref _currentVersion);
        if (remoteVersion <= currentVersion)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.PackageUrl) || string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            _telemetry.Warn("Manifesto de atualização incompleto: pacote ou hash ausentes.");
            return;
        }

        var packagePath = await DownloadPackageAsync(manifest.PackageUrl).ConfigureAwait(false);
        if (packagePath is null)
        {
            return;
        }

        try
        {
            var hashValid = await ValidateHashAsync(packagePath, manifest.Sha256).ConfigureAwait(false);
            if (!hashValid)
            {
                _telemetry.Warn("Hash do pacote de atualização não confere. Atualização ignorada.");
                return;
            }

            var stagingDirectory = PrepareStagingDirectory();
            try
            {
                ExtractPackage(packagePath, stagingDirectory);
                ApplyUpdate(stagingDirectory);
                Volatile.Write(ref _currentVersion, remoteVersion);
                _telemetry.Info($"Atualização aplicada com sucesso para a versão {remoteVersion}.");
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
        finally
        {
            TryDeleteFile(packagePath);
        }
    }

    private async Task<UpdateManifest?> FetchManifestAsync(string manifestUrl)
    {
        try
        {
            using var response = await _httpClient.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _telemetry.Warn($"Falha ao obter manifesto de atualização: {(int)response.StatusCode} {response.ReasonPhrase}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, ManifestSerializerOptions).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Erro ao obter o manifesto de atualização.", ex);
            return null;
        }
    }

    private async Task<string?> DownloadPackageAsync(string packageUrl)
    {
        try
        {
            using var response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _telemetry.Warn($"Falha ao baixar o pacote de atualização: {(int)response.StatusCode} {response.ReasonPhrase}.");
                return null;
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"mieruka-update-{Guid.NewGuid():N}.zip");
            await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var destination = File.Create(tempFile);
            await source.CopyToAsync(destination).ConfigureAwait(false);
            return tempFile;
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Erro ao baixar o pacote de atualização.", ex);
            return null;
        }
    }

    private async Task<bool> ValidateHashAsync(string packagePath, string expectedHash)
    {
        try
        {
            await using var stream = File.OpenRead(packagePath);
            using var sha256 = SHA256.Create();
            var computed = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
            var actual = Convert.ToHexString(computed);
            return string.Equals(actual, NormalizeHash(expectedHash), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Erro ao validar hash do pacote de atualização.", ex);
            return false;
        }
    }

    private void ExtractPackage(string packagePath, string stagingDirectory)
    {
        try
        {
            ZipFile.ExtractToDirectory(packagePath, stagingDirectory, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Erro ao extrair o pacote de atualização.", ex);
            throw;
        }
    }

    private void ApplyUpdate(string stagingDirectory)
    {
        foreach (var directory in Directory.GetDirectories(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingDirectory, directory);
            var target = Path.Combine(_applicationDirectory, relative);
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingDirectory, file);
            var destination = Path.Combine(_applicationDirectory, relative);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            try
            {
                File.Copy(file, destination, overwrite: true);
            }
            catch (Exception ex)
            {
                _telemetry.Warn($"Falha ao substituir o arquivo '{relative}'.", ex);
                throw;
            }
        }
    }

    private static string NormalizeHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var pos = 0;
        foreach (var ch in value)
        {
            if (Uri.IsHexDigit(ch))
            {
                buffer[pos++] = ch;
            }
        }

        return new string(buffer[..pos]);
    }

    private static string ResolveApplicationDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            return directory;
        }

        return AppContext.BaseDirectory;
    }

    private static Version ResolveCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static TimeSpan ResolveInterval(int configuredMinutes)
    {
        var minutes = configuredMinutes;
        if (minutes < MinimumIntervalMinutes)
        {
            minutes = MinimumIntervalMinutes;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static string PrepareStagingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mieruka-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static readonly ILogger Logger = Log.ForContext<UpdaterService>();

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete directory {Path}.", path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete file {Path}.", path);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UpdaterService));
        }
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        if (!string.IsNullOrWhiteSpace(value) && Version.TryParse(value, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0, 0);
        return false;
    }

    private sealed record class UpdateManifest
    {
        public string? Version { get; init; }

        public string? PackageUrl { get; init; }

        public string? Sha256 { get; init; }
    }

}
