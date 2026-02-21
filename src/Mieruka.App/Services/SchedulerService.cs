using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Services;
using Mieruka.Core.Data;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.App.Services;

/// <summary>
/// Manages scheduled start/stop of the Orchestrator based on time-of-day configuration.
/// </summary>
public sealed class SchedulerService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SchedulerService>();
    private const string SettingsKey = "Schedule";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly Orchestrator _orchestrator;
    private Timer? _timer;
    private ScheduleConfig _config = new();
    private bool _disposed;
    private bool _schedulerStarted;
    private bool _schedulerStopped;

    public SchedulerService(Orchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    /// <summary>
    /// Loads the schedule configuration and starts monitoring.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);

        if (!_config.Enabled)
        {
            Logger.Information("Scheduler is disabled.");
            return;
        }

        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, CheckInterval);
        Logger.Information("Scheduler started. Start={Start}, Stop={Stop}",
            _config.StartTime?.ToString() ?? "none",
            _config.StopTime?.ToString() ?? "none");
    }

    /// <summary>
    /// Stops the scheduler.
    /// </summary>
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        _schedulerStarted = false;
        _schedulerStopped = false;
    }

    /// <summary>
    /// Applies new schedule configuration.
    /// </summary>
    public async Task ApplyConfigAsync(ScheduleConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        await SaveConfigAsync(config, cancellationToken).ConfigureAwait(false);

        Stop();
        if (config.Enabled)
        {
            _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, CheckInterval);
        }
    }

    /// <summary>
    /// Returns the current schedule configuration.
    /// </summary>
    public ScheduleConfig GetCurrentConfig() => _config;

    private async void OnTimerTick(object? state)
    {
        if (_disposed || !_config.Enabled) return;

        try
        {
            var currentTime = DateTime.Now;
            var now = TimeOnly.FromDateTime(currentTime);
            var today = currentTime.DayOfWeek;

            if (_config.DaysOfWeek.Count > 0 && !_config.DaysOfWeek.Contains(today))
            {
                return;
            }

            var shouldRun = IsWithinSchedule(now);

            if (shouldRun && _orchestrator.State is OrchestratorState.Init or OrchestratorState.Ready)
            {
                if (!_schedulerStarted)
                {
                    _schedulerStarted = true;
                    _schedulerStopped = false;
                    Logger.Information("Scheduler triggering orchestrator start at {Time}.", now);
                    await _orchestrator.StartAsync().ConfigureAwait(false);
                }
            }
            else if (!shouldRun && _orchestrator.State is OrchestratorState.Running or OrchestratorState.Recovering)
            {
                if (!_schedulerStopped)
                {
                    _schedulerStopped = true;
                    _schedulerStarted = false;
                    Logger.Information("Scheduler triggering orchestrator stop at {Time}.", now);
                    await _orchestrator.StopAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Scheduler tick failed.");
        }
    }

    private bool IsWithinSchedule(TimeOnly now)
    {
        if (_config.StartTime is null && _config.StopTime is null)
        {
            return true;
        }

        var start = _config.StartTime ?? TimeOnly.MinValue;
        var stop = _config.StopTime ?? TimeOnly.MaxValue;

        if (start <= stop)
        {
            return now >= start && now <= stop;
        }

        // Crosses midnight (e.g., 22:00 → 06:00)
        return now >= start || now <= stop;
    }

    private static async Task<ScheduleConfig> LoadConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var db = new MierukaDbContext();
            var setting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.AppSettings, s => s.Key == SettingsKey, cancellationToken)
                .ConfigureAwait(false);

            if (setting is not null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                return JsonSerializer.Deserialize<ScheduleConfig>(setting.ValueJson) ?? new ScheduleConfig();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load schedule config.");
        }

        return new ScheduleConfig();
    }

    private static async Task SaveConfigAsync(ScheduleConfig config, CancellationToken cancellationToken)
    {
        try
        {
            using var db = new MierukaDbContext();
            var json = JsonSerializer.Serialize(config);
            var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.AppSettings, s => s.Key == SettingsKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = SettingsKey, ValueJson = json });
            }
            else
            {
                existing.ValueJson = json;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to save schedule config.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
