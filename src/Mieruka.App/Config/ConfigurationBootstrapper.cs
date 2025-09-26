using System;
using System.IO;
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

    /// <summary>
    /// Ensures the configuration file exists on disk and returns its path.
    /// </summary>
    public static string EnsureConfigurationFile()
    {
        var configurationPath = ResolveConfigurationPath();
        Log.Information("Inicializando configuração em {ConfigurationPath}", configurationPath);
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
        if (!File.Exists(samplePath))
        {
            var exception = new FileNotFoundException(
                $"Sample configuration '{SampleRelativePath}' was not copied to the output directory.",
                samplePath);
            Log.Fatal(exception, "A amostra de configuração não foi encontrada em {SamplePath}", samplePath);
            throw exception;
        }

        Log.Warning(
            "Nenhum arquivo de configuração encontrado. Copiando amostra de {SamplePath} para {ConfigurationPath}",
            samplePath,
            configurationPath);

        try
        {
            File.Copy(samplePath, configurationPath, overwrite: false);
            Log.Warning("Arquivo de configuração criado a partir da amostra em {ConfigurationPath}", configurationPath);
        }
        catch (IOException) when (File.Exists(configurationPath))
        {
            // The configuration was created by another process concurrently. Nothing else to do.
            Log.Information(
                "Arquivo de configuração detectado durante a cópia em {ConfigurationPath}. Outra instância já criou o arquivo.",
                configurationPath);
        }

        return configurationPath;
    }

    /// <summary>
    /// Creates a JSON store that targets the persisted configuration file.
    /// </summary>
    public static JsonStore<GeneralConfig> CreateStore()
    {
        var path = EnsureConfigurationFile();
        Log.Information("Carregando configurações de {ConfigurationPath}", path);
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

    private static string ResolveConfigurationPath()
    {
        return Path.Combine(ResolveConfigurationDirectory(), ConfigFileName);
    }
}
