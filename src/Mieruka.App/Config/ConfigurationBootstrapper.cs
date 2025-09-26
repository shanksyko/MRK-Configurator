using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.App.Config;

/// <summary>
/// Handles discovery and initialization of the application configuration file.
/// </summary>
internal static class ConfigurationBootstrapper
{
    private const string ConfigFileName = "appsettings.json";
    private const string SampleRelativePath = "config/appsettings.sample.json";
    private const string RootFolderName = "Mieruka";
    private const string ConfigFolderName = "config";
    private const string MinimalConfigurationPayload = "{\"profiles\":[]}";

    /// <summary>
    /// Ensures the configuration file exists on disk and returns its path.
    /// </summary>
    public static string EnsureConfigurationFile()
    {
        var configurationPath = ResolveConfigurationPath();
        if (File.Exists(configurationPath))
        {
            return configurationPath;
        }

        var directory = Path.GetDirectoryName(configurationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var samplePath = Path.Combine(AppContext.BaseDirectory, SampleRelativePath);
        if (File.Exists(samplePath))
        {
            try
            {
                File.Copy(samplePath, configurationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(configurationPath))
            {
                // The configuration was created by another process concurrently. Nothing else to do.
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Não foi possível copiar o arquivo de configuração de exemplo. Um arquivo mínimo será criado.");
                WriteMinimalConfiguration(configurationPath);
            }
        }
        else
        {
            Log.Warning(
                "Arquivo de configuração de exemplo não encontrado em '{SamplePath}'. Um arquivo mínimo será criado.",
                samplePath);
            WriteMinimalConfiguration(configurationPath);
        }

        if (!File.Exists(configurationPath))
        {
            WriteMinimalConfiguration(configurationPath);
        }

        return configurationPath;
    }

    /// <summary>
    /// Creates a JSON store that targets the persisted configuration file.
    /// </summary>
    public static JsonStore<GeneralConfig> CreateStore()
    {
        var path = EnsureConfigurationFile();
        return new JsonStore<GeneralConfig>(path);
    }

    /// <summary>
    /// Loads the configuration from disk, returning a default instance when absent.
    /// </summary>
    /// <param name="store">Backing store that points to the configuration file.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public static async Task<GeneralConfig> LoadAsync(JsonStore<GeneralConfig> store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var config = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return config ?? new GeneralConfig();
    }

    /// <summary>
    /// Resolves the directory that should contain the configuration artifacts.
    /// </summary>
    public static string ResolveConfigurationDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, RootFolderName, ConfigFolderName);
    }

    public static string ResolveConfigurationPath()
    {
        return Path.Combine(ResolveConfigurationDirectory(), ConfigFileName);
    }

    /// <summary>
    /// Validates the configuration file ensuring it contains valid JSON.
    /// </summary>
    /// <param name="configurationPath">Path to the configuration file.</param>
    public static void ValidateConfigurationFile(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        if (!File.Exists(configurationPath))
        {
            throw new FileNotFoundException("Configuration file not found.", configurationPath);
        }

        try
        {
            using var stream = File.OpenRead(configurationPath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var payload = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new JsonException($"Configuration file '{configurationPath}' is empty.");
            }

            using var document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Invalid JSON in configuration file '{configurationPath}'.", ex);
        }
    }

    private static void WriteMinimalConfiguration(string configurationPath)
    {
        try
        {
            File.WriteAllText(configurationPath, MinimalConfigurationPayload, Encoding.UTF8);
        }
        catch (IOException) when (File.Exists(configurationPath))
        {
            // Another process created the file concurrently. Nothing else to do.
        }
    }
}
