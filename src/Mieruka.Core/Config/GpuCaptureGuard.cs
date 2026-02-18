using System;
using Serilog;

namespace Mieruka.Core.Config;

/// <summary>
/// Centralizes the decision of whether GPU backed capture should be used.
/// </summary>
public static class GpuCaptureGuard
{
    private static readonly object Gate = new();

    private static bool _initialized;
    private static bool _gpuAllowed;
    private static bool _gpuPermanentlyDisabled;
    private static string? _disabledReason;
    private static Func<bool>? _environmentProbe;
    private static Action<string>? _onPermanentDisable;

    /// <summary>
    /// Initializes the guard using the provided configuration and environment probe.
    /// </summary>
    /// <param name="configurationAllowsGpu">Indicates whether the persisted configuration enables GPU usage.</param>
    /// <param name="environmentProbe">Optional delegate responsible for validating the host capabilities.</param>
    /// <param name="onPermanentDisable">Optional callback invoked whenever the guard permanently disables GPU usage.</param>
    public static void Initialize(
        bool configurationAllowsGpu,
        Func<bool>? environmentProbe = null,
        Action<string>? onPermanentDisable = null)
    {
        lock (Gate)
        {
            if (onPermanentDisable is not null)
            {
                _onPermanentDisable = onPermanentDisable;
            }

            _environmentProbe ??= environmentProbe;

            if (_initialized)
            {
                if (!configurationAllowsGpu && !_gpuPermanentlyDisabled)
                {
                    _gpuAllowed = false;
                    _disabledReason = "config";
                    Log.Information("GPU guard: disabled by configuration preferences.");
                }

                return;
            }

            _initialized = true;

            if (!configurationAllowsGpu)
            {
                _gpuAllowed = false;
                _disabledReason = "config";
                Log.Information("GPU guard: disabled by configuration preferences.");
                return;
            }

            if (_environmentProbe is not null)
            {
                bool environmentReady;
                try
                {
                    environmentReady = _environmentProbe();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "GPU guard: environment probe failed; disabling GPU capture.");
                    environmentReady = false;
                }

                if (!environmentReady)
                {
                    _gpuAllowed = false;
                    _disabledReason = "environment";
                    Log.Information("GPU guard: disabled by environment check.");
                    return;
                }
            }

            _gpuAllowed = true;
            _disabledReason = null;
            Log.Information("GPU guard: GPU capture allowed on this host.");
        }
    }

    /// <summary>
    /// Determines whether GPU backed capture can currently be used.
    /// This is a lock-free read; the flags are set under lock but once
    /// initialized they are effectively read-only, making volatile reads safe.
    /// </summary>
    /// <returns><c>true</c> when GPU usage is allowed; otherwise, <c>false</c>.</returns>
    public static bool CanUseGpu()
    {
        return Volatile.Read(ref _initialized)
            && Volatile.Read(ref _gpuAllowed)
            && !Volatile.Read(ref _gpuPermanentlyDisabled);
    }

    /// <summary>
    /// Permanently disables GPU backed capture for the lifetime of the process.
    /// </summary>
    /// <param name="reason">Reason describing why GPU was disabled.</param>
    /// <returns><c>true</c> when the guard transitioned to the disabled state; otherwise, <c>false</c>.</returns>
    public static bool DisableGpuPermanently(string reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;

        Action<string>? callback = null;
        lock (Gate)
        {
            if (_gpuPermanentlyDisabled)
            {
                return false;
            }

            _gpuPermanentlyDisabled = true;
            _gpuAllowed = false;
            _disabledReason = reason;
            callback = _onPermanentDisable;

            Log.Error("GPU guard: permanently disabling GPU capture. Reason={Reason}", reason);
        }

        if (callback is not null)
        {
            try
            {
                callback(reason);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GPU guard: permanent disable callback failed.");
            }
        }

        return true;
    }

    /// <summary>
    /// Registers a callback invoked whenever the guard permanently disables GPU usage.
    /// </summary>
    /// <param name="callback">Callback receiving the reason for the disable action.</param>
    public static void RegisterPermanentDisableCallback(Action<string> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (Gate)
        {
            _onPermanentDisable = callback;
        }
    }

    /// <summary>
    /// Provides a descriptive reason explaining why GPU usage is currently blocked.
    /// </summary>
    /// <returns>Reason text when available; otherwise, <see langword="null"/>.</returns>
    public static string? GetDisabledReason()
    {
        return Volatile.Read(ref _gpuAllowed) && !Volatile.Read(ref _gpuPermanentlyDisabled)
            ? null
            : Volatile.Read(ref _disabledReason);
    }
}
