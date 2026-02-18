using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Mieruka.App;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Config;

/// <summary>
/// Performs validation on <see cref="GeneralConfig"/> instances, surfacing errors and warnings
/// that could prevent the player from operating correctly.
/// </summary>
internal sealed partial class ConfigValidator
{
    private static readonly IReadOnlyList<string> ChromeDriverCandidates = new[]
    {
        "chromedriver.exe",
        "chromedriver",
    };

    private static readonly IReadOnlyList<string> EdgeDriverCandidates = new[]
    {
        "msedgedriver.exe",
        "msedgedriver",
    };

    private static readonly IReadOnlyList<string> GeckoDriverCandidates = new[]
    {
        "geckodriver.exe",
        "geckodriver",
    };

    private static readonly IReadOnlyList<string> BraveDriverCandidates = ChromeDriverCandidates;

    private readonly string _baseDirectory;
    private readonly IReadOnlyList<string> _driverProbePaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigValidator"/> class.
    /// </summary>
    /// <param name="baseDirectory">Base directory used to resolve relative paths.</param>
    /// <param name="driverProbePaths">Optional set of directories that should be inspected when looking for WebDriver binaries.</param>
    public ConfigValidator(string? baseDirectory = null, IEnumerable<string>? driverProbePaths = null)
    {
        _baseDirectory = ResolveDirectory(baseDirectory) ?? AppContext.BaseDirectory;

        var probes = new List<string>();
        if (!string.IsNullOrWhiteSpace(_baseDirectory))
        {
            probes.Add(_baseDirectory);

            var driversDirectory = Path.Combine(_baseDirectory, "drivers");
            probes.Add(driversDirectory);
        }

        if (driverProbePaths is not null)
        {
            foreach (var probe in driverProbePaths)
            {
                var resolved = ResolveDirectory(probe);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    probes.Add(resolved);
                }
            }
        }

        var environmentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in environmentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var resolved = ResolveDirectory(entry);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                probes.Add(resolved);
            }
        }

        _driverProbePaths = probes
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Validates the supplied configuration and returns a report describing all detected issues.
    /// </summary>
    /// <param name="config">Configuration that should be validated.</param>
    /// <param name="environmentMonitors">Monitors currently available in the environment.</param>
    /// <returns>Validation report containing errors and warnings.</returns>
    public ConfigValidationReport Validate(GeneralConfig config, IReadOnlyList<MonitorInfo>? environmentMonitors)
    {
        ArgumentNullException.ThrowIfNull(config);

        var issues = new List<ConfigValidationIssue>();
        var monitors = BuildMonitorLookup(config.Monitors, environmentMonitors);

        ValidateMonitors(config.Monitors, issues);
        ValidateApplications(config.Applications, monitors, issues);
        ValidateSites(config.Sites, monitors, issues);
        ValidateCycle(config, issues);
        ValidateDriverAvailability(config.Sites, issues);
        ValidateSecurity(config, issues);

        return new ConfigValidationReport(new ReadOnlyCollection<ConfigValidationIssue>(issues));
    }

    private static string? ResolveDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<MonitorInfo> BuildMonitorLookup(IList<MonitorInfo>? configured, IReadOnlyList<MonitorInfo>? environment)
    {
        var lookup = new Dictionary<string, MonitorInfo>(StringComparer.OrdinalIgnoreCase);

        void AddMonitor(MonitorInfo? monitor)
        {
            if (monitor is null)
            {
                return;
            }

            var key = FormatMonitorKey(monitor.Key);
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = monitor;
            }
        }

        if (configured is not null)
        {
            foreach (var monitor in configured)
            {
                AddMonitor(monitor);
            }
        }

        if (environment is not null)
        {
            foreach (var monitor in environment)
            {
                AddMonitor(monitor);
            }
        }

        return new ReadOnlyCollection<MonitorInfo>(lookup.Values.ToList());
    }

    private static string FormatMonitorKey(MonitorKey key)
    {
        return string.Join("|",
            key.DeviceId?.Trim() ?? string.Empty,
            key.DisplayIndex,
            key.AdapterLuidHigh,
            key.AdapterLuidLow,
            key.TargetId);
    }

    private static bool KeysEqual(MonitorKey left, MonitorKey right)
    {
        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private void ValidateMonitors(IList<MonitorInfo>? monitors, ICollection<ConfigValidationIssue> issues)
    {
        if (monitors is null || monitors.Count == 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, "Nenhum monitor configurado."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            if (monitor is null)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, "Entrada de monitor inválida na configuração."));
                continue;
            }

            var key = FormatMonitorKey(monitor.Key);
            if (!seen.Add(key))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Monitor duplicado: {monitor.Name}", monitor.Name));
            }

            if (monitor.Width <= 0 || monitor.Height <= 0)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Dimensões inválidas para o monitor '{monitor.Name}'.", monitor.Name));
            }
        }
    }

    private void ValidateApplications(IList<AppConfig>? applications, IReadOnlyList<MonitorInfo> monitors, ICollection<ConfigValidationIssue> issues)
    {
        if (applications is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in applications)
        {
            if (app is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(app.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, "Aplicativo sem identificador definido."));
                continue;
            }

            if (!seen.Add(app.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Aplicativo '{app.Id}' está duplicado.", app.Id));
            }

            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Aplicativo '{app.Id}' não possui caminho configurado.", app.Id));
            }
            else if (!ExecutableExists(app.ExecutablePath))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Executável não encontrado: {app.ExecutablePath}.", app.Id));
            }

            ValidateWindow(app.Window, $"Aplicativo '{app.Id}'", monitors, issues);
            ValidateWatchdog(app.Watchdog, $"Aplicativo '{app.Id}'", issues);
        }
    }

    private void ValidateSites(IList<SiteConfig>? sites, IReadOnlyList<MonitorInfo> monitors, ICollection<ConfigValidationIssue> issues)
    {
        if (sites is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites)
        {
            if (site is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(site.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, "Site sem identificador definido."));
                continue;
            }

            if (!seen.Add(site.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Site '{site.Id}' está duplicado.", site.Id));
            }

            if (string.IsNullOrWhiteSpace(site.Url) || !Uri.TryCreate(site.Url, UriKind.Absolute, out _))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Site '{site.Id}' possui URL inválida.", site.Id));
            }

            if (!string.IsNullOrWhiteSpace(site.UserDataDirectory) && !DirectoryExists(site.UserDataDirectory))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Diretório de perfil não encontrado: {site.UserDataDirectory}.", site.Id));
            }

            if (!string.IsNullOrWhiteSpace(site.ProfileDirectory) && site.ProfileDirectory.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Nome de perfil inválido: {site.ProfileDirectory}.", site.Id));
            }

            ValidateLogin(site.Login, site.Id, issues);
            ValidateWindow(site.Window, $"Site '{site.Id}'", monitors, issues);
            ValidateWatchdog(site.Watchdog, $"Site '{site.Id}'", issues);

            if (site.ReloadIntervalSeconds is { } reloadInterval && reloadInterval <= 0)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Intervalo de recarga inválido ({reloadInterval}).", site.Id));
            }
        }
    }

    private void ValidateLogin(LoginProfile? login, string siteId, ICollection<ConfigValidationIssue> issues)
    {
        if (login is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(login.UserSelector) || string.IsNullOrWhiteSpace(login.PassSelector))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Site '{siteId}' possui seletores de login incompletos.", siteId));
        }

        if (string.IsNullOrWhiteSpace(login.SubmitSelector) && string.IsNullOrWhiteSpace(login.Script))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Site '{siteId}' não possui ação de envio configurada para o login.", siteId));
        }

        if (login.TimeoutSeconds <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Site '{siteId}' possui tempo limite de login inválido ({login.TimeoutSeconds}).", siteId));
        }
    }

    private void ValidateWatchdog(WatchdogSettings? settings, string context, ICollection<ConfigValidationIssue> issues)
    {
        if (settings?.HealthCheck is not { } check)
        {
            return;
        }

        if (check.IntervalSeconds <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"{context} possui intervalo de health check inválido ({check.IntervalSeconds}).", context));
        }

        if (check.TimeoutSeconds <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"{context} possui timeout de health check inválido ({check.TimeoutSeconds}).", context));
        }

        if (check.Type is HealthCheckKind.None)
        {
            return;
        }

        var targetUrl = check.Url;
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"{context} não definiu URL para o health check.", context));
        }
        else if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} possui URL de health check inválida: {targetUrl}.", context));
        }

        if (check.Type is HealthCheckKind.Dom)
        {
            if (string.IsNullOrWhiteSpace(check.DomSelector) && string.IsNullOrWhiteSpace(check.ContainsText))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} requer seletor ou texto esperado para o health check DOM.", context));
            }

            if (!string.IsNullOrWhiteSpace(check.DomSelector))
            {
                try
                {
                    InputSanitizer.SanitizeSelector(check.DomSelector, 512);
                }
                catch (Exception ex)
                {
                    issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} possui seletor DOM inválido: {ex.Message}.", context));
                }
            }

            if (!string.IsNullOrWhiteSpace(check.ContainsText))
            {
                try
                {
                    InputSanitizer.EnsureSafeAscii(check.ContainsText, 512, nameof(check.ContainsText));
                }
                catch (Exception ex)
                {
                    issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} possui texto esperado inválido: {ex.Message}.", context));
                }
            }
        }
    }

    private void ValidateWindow(WindowConfig? window, string context, IReadOnlyList<MonitorInfo> monitors, ICollection<ConfigValidationIssue> issues)
    {
        if (window is null)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"{context} não possui configurações de janela.", context));
            return;
        }

        if (window.Width.HasValue && window.Width <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} possui largura inválida ({window.Width}).", context));
        }

        if (window.Height.HasValue && window.Height <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} possui altura inválida ({window.Height}).", context));
        }

        if (!IsMonitorConfigured(window.Monitor))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} não possui monitor atribuído.", context));
            return;
        }

        if (!MonitorExists(monitors, window.Monitor))
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"{context} faz referência a um monitor indisponível.", context));
        }
    }

    private static bool IsMonitorConfigured(MonitorKey key)
    {
        return !string.IsNullOrWhiteSpace(key.DeviceId)
            || key.DisplayIndex != 0
            || key.AdapterLuidHigh != 0
            || key.AdapterLuidLow != 0
            || key.TargetId != 0;
    }

    private static bool MonitorExists(IEnumerable<MonitorInfo> monitors, MonitorKey key)
    {
        if (monitors is null) return false;
        foreach (var monitor in monitors)
        {
            if (monitor is not null && KeysEqual(monitor.Key, key))
                return true;
        }
        return false;
    }

    private void ValidateCycle(GeneralConfig config, ICollection<ConfigValidationIssue> issues)
    {
        var cycle = config.Cycle;
        if (cycle is null)
        {
            return;
        }

        if (cycle.DefaultDurationSeconds <= 0)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, "Duração padrão da rotação inválida.", "Cycle"));
        }

        var applications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.Applications is not null)
        {
            foreach (var application in config.Applications)
            {
                if (!string.IsNullOrWhiteSpace(application?.Id))
                {
                    applications.Add(application.Id);
                }
            }
        }

        var sites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.Sites is not null)
        {
            foreach (var site in config.Sites)
            {
                if (!string.IsNullOrWhiteSpace(site?.Id))
                {
                    sites.Add(site.Id);
                }
            }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in cycle.Items ?? Array.Empty<CycleItem>())
        {
            if (item is null || !item.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, "Item da rotação sem identificador definido.", "Cycle"));
                continue;
            }

            if (!seen.Add(item.Id))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Item da rotação '{item.Id}' está duplicado.", item.Id));
            }

            if (!TryParseKind(item.TargetType, out var kind))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Item da rotação '{item.Id}' possui tipo inválido '{item.TargetType}'.", item.Id));
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.TargetId))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Item da rotação '{item.Id}' não especifica um alvo.", item.Id));
                continue;
            }

            var exists = kind switch
            {
                EntryKind.Application => applications.Contains(item.TargetId),
                EntryKind.Site => sites.Contains(item.TargetId),
                _ => false,
            };

            if (!exists)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Item da rotação '{item.Id}' referencia '{item.TargetId}', que não existe.", item.Id));
            }

            if (item.DurationSeconds <= 0 && cycle.DefaultDurationSeconds <= 0)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"Item da rotação '{item.Id}' não possui duração válida.", item.Id));
            }
        }
    }

    private void ValidateDriverAvailability(IList<SiteConfig>? sites, ICollection<ConfigValidationIssue> issues)
    {
        if (sites is null || sites.Count == 0)
        {
            return;
        }

        var requiredBrowsers = new HashSet<BrowserType>();
        foreach (var site in sites)
        {
            if (site is null)
            {
                continue;
            }

            requiredBrowsers.Add(site.Browser);
        }

        foreach (var browser in requiredBrowsers)
        {
            if (browser is not (BrowserType.Chrome or BrowserType.Edge or BrowserType.Firefox or BrowserType.Brave))
            {
                continue;
            }

            if (!DriverAvailable(browser))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Driver do navegador {browser} não foi encontrado.", browser.ToString()));
            }
        }
    }

    private bool DriverAvailable(BrowserType browser)
    {
        var candidates = browser switch
        {
            BrowserType.Chrome => ChromeDriverCandidates,
            BrowserType.Edge => EdgeDriverCandidates,
            BrowserType.Firefox => GeckoDriverCandidates,
            BrowserType.Brave => BraveDriverCandidates,
            _ => Array.Empty<string>(),
        };

        foreach (var directory in _driverProbePaths)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ExecutableExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path);
        }

        var candidate = Path.Combine(_baseDirectory, path);
        return File.Exists(candidate);
    }

    private bool DirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return Directory.Exists(path);
        }

        var candidate = Path.Combine(_baseDirectory, path);
        return Directory.Exists(candidate);
    }

    private static bool TryParseKind(string? value, out EntryKind kind)
    {
        kind = EntryKind.Application;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("Application", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("App", StringComparison.OrdinalIgnoreCase))
        {
            kind = EntryKind.Application;
            return true;
        }

        if (value.Equals("Site", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Web", StringComparison.OrdinalIgnoreCase))
        {
            kind = EntryKind.Site;
            return true;
        }

        return Enum.TryParse(value, true, out kind);
    }
}

/// <summary>
/// Describes the outcome of validating a configuration file.
/// </summary>
internal sealed class ConfigValidationReport
{
    /// <summary>
    /// Gets an empty validation report.
    /// </summary>
    public static ConfigValidationReport Empty { get; } = new(Array.Empty<ConfigValidationIssue>());

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigValidationReport"/> class.
    /// </summary>
    /// <param name="issues">Collection of issues discovered during validation.</param>
    public ConfigValidationReport(IEnumerable<ConfigValidationIssue> issues)
    {
        if (issues is null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        var list = issues.ToList();
        Issues = list.AsReadOnly();
        int errors = 0, warnings = 0;
        foreach (var issue in list)
        {
            if (issue.Severity == ConfigValidationSeverity.Error) errors++;
            else if (issue.Severity == ConfigValidationSeverity.Warning) warnings++;
        }
        ErrorCount = errors;
        WarningCount = warnings;
    }

    /// <summary>
    /// Gets the issues reported by the validator.
    /// </summary>
    public IReadOnlyList<ConfigValidationIssue> Issues { get; }

    /// <summary>
    /// Gets the number of errors detected during validation.
    /// </summary>
    public int ErrorCount { get; }

    /// <summary>
    /// Gets the number of warnings detected during validation.
    /// </summary>
    public int WarningCount { get; }

    /// <summary>
    /// Gets a value indicating whether any critical errors were detected.
    /// </summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>
    /// Gets a value indicating whether any warnings were detected.
    /// </summary>
    public bool HasWarnings => WarningCount > 0;
}

/// <summary>
/// Represents a validation issue found in the configuration.
/// </summary>
/// <param name="Severity">Issue severity.</param>
/// <param name="Message">Human readable description of the problem.</param>
/// <param name="Source">Optional source that originated the issue.</param>
internal sealed record class ConfigValidationIssue(ConfigValidationSeverity Severity, string Message, string? Source = null);

/// <summary>
/// Categorizes validation issues according to their severity.
/// </summary>
internal enum ConfigValidationSeverity
{
    /// <summary>
    /// Critical issue that prevents execution.
    /// </summary>
    Error,

    /// <summary>
    /// Non-blocking issue that should be addressed but does not prevent execution.
    /// </summary>
    Warning,
}
