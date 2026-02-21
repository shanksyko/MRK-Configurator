using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using Mieruka.App.Forms;

namespace Mieruka.App.Services;

/// <summary>
/// Supervises native applications and browser instances, restarting them when necessary and ensuring
/// their windows remain in the expected position.
/// </summary>
internal sealed class WatchdogService : IOrchestrationComponent, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BindingRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly BindingTrayService _bindingService;
    private readonly ITelemetry _telemetry;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private readonly Dictionary<string, AppWatchContext> _applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SiteWatchContext> _sites = new(StringComparer.OrdinalIgnoreCase);

    private BrowserArgumentsSettings? _globalBrowserArguments;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _disposed;
    private bool _hasConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchdogService"/> class.
    /// </summary>
    /// <param name="bindingService">Service responsible for applying window placement.</param>
    /// <param name="telemetry">Optional telemetry sink used to record watchdog events.</param>
    public WatchdogService(BindingTrayService bindingService, ITelemetry? telemetry = null)
    {
        _bindingService = bindingService ?? throw new ArgumentNullException(nameof(bindingService));
        _telemetry = telemetry ?? NullTelemetry.Instance;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Applies the provided configuration, updating the supervised entries.
    /// </summary>
    /// <param name="config">Configuration that describes applications and sites.</param>
    public void ApplyConfiguration(GeneralConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            EnsureNotDisposed();

            _globalBrowserArguments = config.BrowserArguments;
            _hasConfiguration = true;

            UpdateApplicationsLocked(config.Applications);
            UpdateSitesLocked(config.Sites);
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureNotDisposed();

            if (!_hasConfiguration)
            {
                throw new InvalidOperationException("Configuration must be applied before starting the watchdog.");
            }

            if (_monitorTask is not null)
            {
                return Task.CompletedTask;
            }

            _monitorCancellation = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorAsync(_monitorCancellation.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopInternalAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await StopInternalAsync().ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a snapshot of the current status of all monitored entries for the dashboard.
    /// </summary>
    public IReadOnlyList<WatchdogStatusEntry> GetStatusSnapshot()
    {
        var entries = new List<WatchdogStatusEntry>();

        lock (_gate)
        {
            foreach (var context in _applications.Values)
            {
                var isAlive = context.Process is not null && !context.Process.HasExited;
                entries.Add(new WatchdogStatusEntry
                {
                    Name = context.Config.Name ?? context.Config.Id,
                    Type = "App",
                    ProcessId = isAlive ? context.Process!.Id : 0,
                    IsAlive = isAlive,
                    FailureCount = context.FailureCount,
                    LastHealthCheck = context.NextHealthCheck > DateTimeOffset.MinValue ? context.NextHealthCheck : null,
                });
            }

            foreach (var context in _sites.Values)
            {
                var isAlive = context.Process is not null && !context.Process.HasExited;
                entries.Add(new WatchdogStatusEntry
                {
                    Name = context.Config.Id,
                    Type = "Site",
                    ProcessId = isAlive ? context.Process!.Id : 0,
                    IsAlive = isAlive,
                    FailureCount = context.FailureCount,
                    LastHealthCheck = context.NextHealthCheck > DateTimeOffset.MinValue ? context.NextHealthCheck : null,
                });
            }
        }

        return entries;
    }

    /// <summary>
    /// Asynchronously releases the resources associated with the service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);

        lock (_gate)
        {
            foreach (var context in _applications.Values)
            {
                context.Dispose();
            }

            foreach (var context in _sites.Values)
            {
                context.Dispose();
            }

            _applications.Clear();
            _sites.Clear();
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Releases the resources associated with the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopInternalSync();

        lock (_gate)
        {
            foreach (var context in _applications.Values)
            {
                context.Dispose();
            }

            foreach (var context in _sites.Values)
            {
                context.Dispose();
            }

            _applications.Clear();
            _sites.Clear();
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        // Reuse lists across iterations to avoid repeated allocations.
        var apps = new List<AppWatchContext>();
        var sites = new List<SiteWatchContext>();

        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                apps.Clear();
                apps.AddRange(_applications.Values);
                sites.Clear();
                sites.AddRange(_sites.Values);
            }

            var timestamp = DateTimeOffset.UtcNow;

            foreach (var app in apps)
            {
                await MonitorApplicationAsync(app, timestamp, cancellationToken).ConfigureAwait(false);
            }

            foreach (var site in sites)
            {
                await MonitorSiteAsync(site, timestamp, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(MonitorInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task MonitorApplicationAsync(AppWatchContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!context.Settings.Enabled)
        {
            context.ReleaseProcess();
            return;
        }

        if (!EnsureApplicationProcess(context, now))
        {
            if (context.Config.AutoStart)
            {
                await TryRestartApplicationAsync(context, now, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        EnsureApplicationBinding(context, now);
        await EnsureHealthAsync(context, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task MonitorSiteAsync(SiteWatchContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!context.Settings.Enabled)
        {
            context.ReleaseProcess();
            return;
        }

        if (!EnsureSiteProcess(context, now))
        {
            await TryRestartSiteAsync(context, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureSiteBinding(context, now);
        await EnsureHealthAsync(context, now, cancellationToken).ConfigureAwait(false);

        TryPeriodicReload(context, now);
    }

    private void TryPeriodicReload(SiteWatchContext context, DateTimeOffset now)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var reloadIntervalSeconds = context.Config.ReloadIntervalSeconds;
        if (reloadIntervalSeconds is null or <= 0)
        {
            return;
        }

        var reloadInterval = TimeSpan.FromSeconds(reloadIntervalSeconds.Value);

        if (context.LastReloadTime != DateTimeOffset.MinValue && now - context.LastReloadTime < reloadInterval)
        {
            return;
        }

        // Don't reload while the browser is still in its restart grace period.
        if (now < context.HealthCheckGraceUntil)
        {
            return;
        }

        var handle = context.LastHandle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const uint WmKeyDown = 0x0100;
        const uint WmKeyUp = 0x0101;
        const int VkF5 = 0x74;

        try
        {
            User32.PostMessage(handle, WmKeyDown, new IntPtr(VkF5), IntPtr.Zero);
            User32.PostMessage(handle, WmKeyUp, new IntPtr(VkF5), IntPtr.Zero);
            context.LastReloadTime = now;
        }
        catch (Exception ex)
        {
            _telemetry.Info($"Periodic reload interop failed for site '{context.Config.Id}'.", ex);
        }
    }

    private bool EnsureApplicationProcess(AppWatchContext context, DateTimeOffset now)
    {
        if (IsProcessAlive(context.Process))
        {
            return true;
        }

        if (context.Process is not null)
        {
            _telemetry.Warn($"Detected that application '{context.Config.Id}' (PID {context.Process.Id}) has exited.");
        }

        context.ReleaseProcess();

        if (TryAttachApplicationProcess(context))
        {
            context.ResetAfterRestart(now);
            return true;
        }

        return false;
    }

    private bool EnsureSiteProcess(SiteWatchContext context, DateTimeOffset now)
    {
        if (IsProcessAlive(context.Process))
        {
            return true;
        }

        if (context.Process is not null)
        {
            _telemetry.Warn($"Detected that site '{context.Config.Id}' (PID {context.Process.Id}) has exited.");
        }

        context.ReleaseProcess();

        if (TryAttachSiteProcess(context))
        {
            context.ResetAfterRestart(now);
            return true;
        }

        return false;
    }

    private async Task TryRestartApplicationAsync(AppWatchContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (context.NextAttempt > now)
        {
            return;
        }

        if (!TryStartApplication(context))
        {
            ScheduleRetry(context, now, $"Failed to start application '{context.Config.Id}'.");
            return;
        }

        context.ResetAfterRestart(now);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
    }

    private async Task TryRestartSiteAsync(SiteWatchContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (context.NextAttempt > now)
        {
            return;
        }

        if (!TryStartSite(context))
        {
            ScheduleRetry(context, now, $"Failed to start site '{context.Config.Id}'.");
            return;
        }

        context.ResetAfterRestart(now);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
    }

    private void EnsureApplicationBinding(AppWatchContext context, DateTimeOffset now)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetWindowHandle(context.Process);
        if (handle == IntPtr.Zero)
        {
            if (context.LastHandle != IntPtr.Zero && now - context.LastBindTime >= BindingRefreshInterval)
            {
                if (_bindingService.TryReapplyBinding(EntryKind.Application, context.Config.Id, out var rebound) && rebound != IntPtr.Zero)
                {
                    context.LastHandle = rebound;
                    context.LastBindTime = now;
                }
            }

            return;
        }

        if (context.LastHandle != handle || now - context.LastBindTime >= BindingRefreshInterval)
        {
            _bindingService.Bind(context.Config, handle);
            context.LastHandle = handle;
            context.LastBindTime = now;
        }
    }

    private void EnsureSiteBinding(SiteWatchContext context, DateTimeOffset now)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetWindowHandle(context.Process);
        if (handle == IntPtr.Zero)
        {
            if (context.LastHandle != IntPtr.Zero && now - context.LastBindTime >= BindingRefreshInterval)
            {
                if (_bindingService.TryReapplyBinding(EntryKind.Site, context.Config.Id, out var rebound) && rebound != IntPtr.Zero)
                {
                    context.LastHandle = rebound;
                    context.LastBindTime = now;
                }
            }

            return;
        }

        if (context.LastHandle != handle || now - context.LastBindTime >= BindingRefreshInterval)
        {
            _bindingService.Bind(context.Config, handle);
            context.LastHandle = handle;
            context.LastBindTime = now;
        }
    }

    private async Task EnsureHealthAsync<TConfig>(WatchContextBase<TConfig> context, DateTimeOffset now, CancellationToken cancellationToken)
        where TConfig : class
    {
        if (!IsProcessAlive(context.Process))
        {
            return;
        }

        var health = context.Settings.HealthCheck;
        if (health is null || health.Type == HealthCheckKind.None)
        {
            return;
        }

        if (context.HealthCheckGraceUntil > now)
        {
            return;
        }

        if (context.NextHealthCheck > now)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, health.IntervalSeconds));
        context.NextHealthCheck = now + interval;

        if (await ExecuteHealthCheckAsync(context, health, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _telemetry.Warn($"Health check failed for {context.DisplayName}. Scheduling restart with backoff.");
        TerminateProcess(context);
        ScheduleRetry(context, now, $"Health check failed for {context.DisplayName}.");
    }

    private async Task<bool> ExecuteHealthCheckAsync<TConfig>(WatchContextBase<TConfig> context, HealthCheckConfig health, CancellationToken cancellationToken)
        where TConfig : class
    {
        var target = ResolveHealthCheckTarget(context, health);
        if (string.IsNullOrWhiteSpace(target))
        {
            return true;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, health.TimeoutSeconds));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            using var response = await _httpClient.GetAsync(target, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _telemetry.Warn($"Health check for {context.DisplayName} returned status {(int)response.StatusCode}.");
                return false;
            }

            if (health.Type == HealthCheckKind.Dom)
            {
                var payload = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(health.DomSelector) &&
                    payload.IndexOf(health.DomSelector, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _telemetry.Warn($"Seletor DOM configurado não foi localizado durante o health check para {context.DisplayName}.");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(health.ContainsText) &&
                    payload.IndexOf(health.ContainsText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _telemetry.Warn($"Texto esperado não foi encontrado durante o health check para {context.DisplayName}.");
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _telemetry.Warn($"Health check for {context.DisplayName} timed out after {timeout}.");
            return false;
        }
        catch (HttpRequestException exception)
        {
            _telemetry.Warn($"Health check for {context.DisplayName} failed due to a request error: {exception.Message}.");
            return false;
        }
        catch (Exception exception)
        {
            _telemetry.Warn($"Unexpected error while executing health check for {context.DisplayName}.", exception);
            return false;
        }
    }

    private static string? ResolveHealthCheckTarget<TConfig>(WatchContextBase<TConfig> context, HealthCheckConfig health)
        where TConfig : class
    {
        if (!string.IsNullOrWhiteSpace(health.Url))
        {
            return health.Url;
        }

        return context.DefaultHealthUrl;
    }

    private bool TryStartApplication(AppWatchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Config.ExecutablePath))
        {
            _telemetry.Warn($"Application '{context.Config.Id}' does not define an executable path. Skipping restart.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = context.Config.ExecutablePath,
                Arguments = context.Config.Arguments ?? string.Empty,
                UseShellExecute = false,
            };

            foreach (var pair in context.Config.EnvironmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                _telemetry.Warn($"Process.Start returned null for application '{context.Config.Id}'.");
                return false;
            }

            context.Process = process;
            context.LastHandle = IntPtr.Zero;
            context.LastBindTime = DateTimeOffset.MinValue;
            _telemetry.Info($"Application '{context.Config.Id}' started (PID {process.Id}).");
            return true;
        }
        catch (Exception exception)
        {
            _telemetry.Error($"Failed to start application '{context.Config.Id}'.", exception);
            return false;
        }
    }

    private bool TryStartSite(SiteWatchContext context)
    {
        try
        {
            var executable = ResolveBrowserExecutable(context.Config.Browser);
            var arguments = BuildBrowserArguments(context.Config);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                _telemetry.Warn($"Process.Start returned null for site '{context.Config.Id}'.");
                return false;
            }

            context.Process = process;
            context.LastHandle = IntPtr.Zero;
            context.LastBindTime = DateTimeOffset.MinValue;
            _telemetry.Info($"Site '{context.Config.Id}' launched using {executable} (PID {process.Id}).");
            return true;
        }
        catch (Exception exception)
        {
            _telemetry.Error($"Failed to start site '{context.Config.Id}'.", exception);
            return false;
        }
    }

    private bool TryAttachApplicationProcess(AppWatchContext context)
    {
        var executablePath = NormalizePath(context.Config.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        foreach (var candidate in Process.GetProcessesByName(processName))
        {
            var keepCandidate = false;

            try
            {
                var module = SafeGetMainModulePath(candidate);
                if (module is null)
                {
                    continue;
                }

                if (!string.Equals(NormalizePath(module), executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                context.Process = candidate;
                _telemetry.Info($"Attached to running application '{context.Config.Id}' (PID {candidate.Id}).");
                keepCandidate = true;
                return true;
            }
            catch (Exception exception)
            {
                _telemetry.Warn($"Failed to inspect process {candidate.Id} while attaching to application '{context.Config.Id}'.", exception);
            }
            finally
            {
                if (!keepCandidate)
                {
                    candidate.Dispose();
                }
            }
        }

        return false;
    }

    private bool TryAttachSiteProcess(SiteWatchContext context)
    {
        var expectedTitle = context.Config.Window?.Title;
        if (string.IsNullOrWhiteSpace(expectedTitle))
        {
            return false;
        }

        // Filter by known browser process names instead of enumerating all processes.
        var browserProcessNames = GetBrowserProcessNames(context.Config.Browser);
        foreach (var processName in browserProcessNames)
        {
            foreach (var candidate in Process.GetProcessesByName(processName))
            {
                var keepCandidate = false;

                try
                {
                    if (string.Equals(candidate.MainWindowTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Process = candidate;
                        _telemetry.Info($"Attached to running site '{context.Config.Id}' (PID {candidate.Id}).");
                        keepCandidate = true;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _telemetry.Info("Cannot access process during site attach.", ex);
                }
                finally
                {
                    if (!keepCandidate)
                    {
                        candidate.Dispose();
                    }
                }
            }
        }

        return false;
    }

    private static readonly string[] ChromeProcessNames = ["chrome", "Google Chrome"];
    private static readonly string[] EdgeProcessNames = ["msedge", "Microsoft Edge"];
    private static readonly string[] FirefoxProcessNames = ["firefox"];
    private static readonly string[] BraveProcessNames = ["brave"];

    private static string[] GetBrowserProcessNames(BrowserType browser)
    {
        return browser switch
        {
            BrowserType.Chrome => ChromeProcessNames,
            BrowserType.Edge => EdgeProcessNames,
            BrowserType.Firefox => FirefoxProcessNames,
            BrowserType.Brave => BraveProcessNames,
            _ => Array.Empty<string>(),
        };
    }

    private void ScheduleRetry<TConfig>(WatchContextBase<TConfig> context, DateTimeOffset now, string reason)
        where TConfig : class
    {
        var delay = context.Backoff;
        if (delay > MaxBackoff)
        {
            delay = MaxBackoff;
        }

        context.NextAttempt = now + delay;
        context.Backoff = IncreaseBackoff(context.Backoff);
        context.FailureCount++;

        _telemetry.Warn($"{reason} Next attempt for {context.DisplayName} in {delay}. (attempt {context.FailureCount})");
    }

    private static TimeSpan IncreaseBackoff(TimeSpan current)
    {
        var doubled = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return doubled < MaxBackoff ? doubled : MaxBackoff;
    }

    private void TerminateProcess<TConfig>(WatchContextBase<TConfig> context)
        where TConfig : class
    {
        var process = context.Process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _telemetry.Info($"Terminating {context.DisplayName} (PID {process.Id}).");

                if (OperatingSystem.IsWindows() && process.CloseMainWindow())
                {
                    if (!process.WaitForExit((int)TerminationWaitTimeout.TotalMilliseconds))
                    {
                        process.Kill(true);
                    }
                }
                else
                {
                    process.Kill(true);
                }
            }
        }
        catch (Exception exception)
        {
            _telemetry.Warn($"Failed to terminate {context.DisplayName} gracefully.", exception);
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception ex)
            {
                _telemetry.Info($"Fallback kill failed for {context.DisplayName}.", ex);
            }
        }
        finally
        {
            context.ReleaseProcess();
        }
    }

    private async Task StopInternalAsync()
    {
        CancellationTokenSource? cancellation = null;
        Task? monitor = null;

        lock (_gate)
        {
            if (_monitorTask is null || _monitorCancellation is null)
            {
                return;
            }

            cancellation = _monitorCancellation;
            monitor = _monitorTask;
            _monitorTask = null;
            _monitorCancellation = null;
        }

        cancellation!.Cancel();

        try
        {
            // Use WhenAny with a timeout to avoid deadlocks when the
            // monitored task tries to marshal back to the UI thread.
            await Task.WhenAny(monitor!, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void StopInternalSync()
    {
        CancellationTokenSource? cancellation = null;
        Task? monitor = null;

        lock (_gate)
        {
            if (_monitorTask is null || _monitorCancellation is null)
            {
                return;
            }

            cancellation = _monitorCancellation;
            monitor = _monitorTask;
            _monitorTask = null;
            _monitorCancellation = null;
        }

        cancellation!.Cancel();

        try
        {
            // Fallback sync path for Dispose only — uses a bounded wait
            // to avoid deadlocking when called from the UI thread.
            monitor!.Wait(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void UpdateApplicationsLocked(IEnumerable<AppConfig> applications)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in applications)
        {
            if (string.IsNullOrWhiteSpace(app.Id))
            {
                continue;
            }

            seen.Add(app.Id);

            if (_applications.TryGetValue(app.Id, out var context))
            {
                context.Update(app);
            }
            else
            {
                _applications[app.Id] = new AppWatchContext(app);
            }
        }

        List<string>? toRemoveApps = null;
        foreach (var id in _applications.Keys)
        {
            if (!seen.Contains(id))
                (toRemoveApps ??= new List<string>()).Add(id);
        }
        if (toRemoveApps is not null)
        {
            foreach (var id in toRemoveApps)
            {
                _applications[id].Dispose();
                _applications.Remove(id);
            }
        }
    }

    private void UpdateSitesLocked(IEnumerable<SiteConfig> sites)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites)
        {
            if (string.IsNullOrWhiteSpace(site.Id))
            {
                continue;
            }

            seen.Add(site.Id);

            if (_sites.TryGetValue(site.Id, out var context))
            {
                context.Update(site);
            }
            else
            {
                _sites[site.Id] = new SiteWatchContext(site);
            }
        }

        List<string>? toRemoveSites = null;
        foreach (var id in _sites.Keys)
        {
            if (!seen.Contains(id))
                (toRemoveSites ??= new List<string>()).Add(id);
        }
        if (toRemoveSites is not null)
        {
            foreach (var id in toRemoveSites)
            {
                _sites[id].Dispose();
                _sites.Remove(id);
            }
        }
    }

    private string BuildBrowserArguments(SiteConfig site)
    {
        var arguments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddArgument(string? argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            if (seen.Add(argument))
            {
                arguments.Add(argument);
            }
        }

        foreach (var argument in GetGlobalBrowserArguments(site.Browser))
        {
            AddArgument(argument);
        }

        foreach (var argument in site.BrowserArguments ?? Array.Empty<string>())
        {
            AddArgument(argument);
        }

        if (!string.IsNullOrWhiteSpace(site.UserDataDirectory) && !ContainsArgument(arguments, "--user-data-dir", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--user-data-dir", site.UserDataDirectory));
        }

        if (!string.IsNullOrWhiteSpace(site.ProfileDirectory) && !ContainsArgument(arguments, "--profile-directory", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--profile-directory", site.ProfileDirectory));
        }

        if (site.KioskMode && !ContainsArgument(arguments, "--kiosk"))
        {
            AddArgument("--kiosk");
        }

        if (site.AppMode)
        {
            if (string.IsNullOrWhiteSpace(site.Url))
            {
                throw new InvalidOperationException($"Site '{site.Id}' requires a URL to use app mode.");
            }

            if (!ContainsArgument(arguments, "--app", matchByPrefix: true))
            {
                AddArgument(FormatArgument("--app", site.Url));
            }
        }

        if (!site.AppMode && !string.IsNullOrWhiteSpace(site.Url))
        {
            AddArgument(site.Url);
        }

        return string.Join(' ', arguments);
    }

    private IEnumerable<string> GetGlobalBrowserArguments(BrowserType browser)
    {
        return _globalBrowserArguments?.ForBrowser(browser) ?? Array.Empty<string>();
    }

    private static bool ContainsArgument(IEnumerable<string> arguments, string name, bool matchByPrefix = false)
    {
        foreach (var argument in arguments)
        {
            if (matchByPrefix)
            {
                if (argument.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }

    private static string ResolveBrowserExecutable(BrowserType browser)
    {
        if (OperatingSystem.IsWindows())
        {
            return browser switch
            {
                BrowserType.Chrome => "chrome.exe",
                BrowserType.Edge => "msedge.exe",
                BrowserType.Firefox => "firefox.exe",
                BrowserType.Brave => "brave.exe",
                _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
            };
        }

        return browser switch
        {
            BrowserType.Chrome => "google-chrome",
            BrowserType.Edge => "microsoft-edge",
            BrowserType.Firefox => "firefox",
            BrowserType.Brave => "brave-browser",
            _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
        };
    }

    private static bool IsProcessAlive(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static IntPtr GetWindowHandle(Process? process)
    {
        if (process is null || !OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            process.Refresh();
            return process.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string? SafeGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WatchdogService));
        }
    }

    private sealed class AppWatchContext : WatchContextBase<AppConfig>
    {
        public AppWatchContext(AppConfig config)
            : base(config, c => c.Watchdog)
        {
        }

        public override EntryKind Kind => EntryKind.Application;

        public override string Id => Config.Id;

        public override string DisplayName => $"application '{Config.Id}'";

        public override string? DefaultHealthUrl => null;
    }

    private sealed class SiteWatchContext : WatchContextBase<SiteConfig>
    {
        public SiteWatchContext(SiteConfig config)
            : base(config, c => c.Watchdog)
        {
        }

        public override EntryKind Kind => EntryKind.Site;

        public override string Id => Config.Id;

        public override string DisplayName => $"site '{Config.Id}'";

        public override string? DefaultHealthUrl => Config.Url;

        /// <summary>Tracks the last time a periodic page reload was sent to this site's browser window.</summary>
        public DateTimeOffset LastReloadTime { get; set; } = DateTimeOffset.MinValue;
    }

    private abstract class WatchContextBase<TConfig> : IDisposable where TConfig : class
    {
        private readonly Func<TConfig, WatchdogSettings> _settingsAccessor;

        protected WatchContextBase(TConfig config, Func<TConfig, WatchdogSettings> settingsAccessor)
        {
            Config = config;
            _settingsAccessor = settingsAccessor;
            Backoff = InitialBackoff;
            NextAttempt = DateTimeOffset.MinValue;
            NextHealthCheck = DateTimeOffset.MinValue;
            HealthCheckGraceUntil = DateTimeOffset.MinValue;
        }

        public TConfig Config { get; private set; }

        public Process? Process { get; set; }

        public TimeSpan Backoff { get; set; }

        public DateTimeOffset NextAttempt { get; set; }

        public DateTimeOffset NextHealthCheck { get; set; }

        public DateTimeOffset HealthCheckGraceUntil { get; set; }

        public IntPtr LastHandle { get; set; }

        public DateTimeOffset LastBindTime { get; set; }

        public int FailureCount { get; set; }

        public abstract EntryKind Kind { get; }

        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        public abstract string? DefaultHealthUrl { get; }

        public WatchdogSettings Settings => _settingsAccessor(Config);

        public void Update(TConfig config)
        {
            Config = config;
        }

        public void ResetAfterRestart(DateTimeOffset timestamp)
        {
            Backoff = InitialBackoff;
            NextAttempt = DateTimeOffset.MinValue;
            FailureCount = 0;
            LastHandle = IntPtr.Zero;
            LastBindTime = DateTimeOffset.MinValue;

            var grace = Math.Max(0, Settings.RestartGracePeriodSeconds);
            var graceInterval = TimeSpan.FromSeconds(grace);
            HealthCheckGraceUntil = timestamp + graceInterval;
            NextHealthCheck = HealthCheckGraceUntil;
        }

        public void ReleaseProcess()
        {
            var process = Process;
            Process = null;

            if (process is not null)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }

            LastHandle = IntPtr.Zero;
            LastBindTime = DateTimeOffset.MinValue;
        }

        public void Dispose()
        {
            ReleaseProcess();
        }
    }

}
