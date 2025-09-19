using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Services;

namespace Mieruka.App.Services;

/// <summary>
/// Represents the possible execution states for the <see cref="Orchestrator"/>.
/// </summary>
public enum OrchestratorState
{
    /// <summary>
    /// Initial state where the orchestrator has not prepared the managed services.
    /// </summary>
    Init,

    /// <summary>
    /// Services are prepared but not actively running.
    /// </summary>
    Ready,

    /// <summary>
    /// Services are running and being monitored.
    /// </summary>
    Running,

    /// <summary>
    /// Services are undergoing recovery procedures.
    /// </summary>
    Recovering,
}

/// <summary>
/// Defines lifecycle operations that must be supported by components controlled by the orchestrator.
/// </summary>
public interface IOrchestrationComponent
{
    /// <summary>
    /// Starts the component, transitioning it to an active state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the component, transitioning it to an idle state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover the component after an error.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task RecoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the lifecycle of monitor, rotation, cycle and watchdog services, guaranteeing
/// that they are started and stopped in a consistent way.
/// </summary>
public sealed class Orchestrator
{
    private readonly IReadOnlyList<ComponentRegistration> _startupOrder;
    private readonly ITelemetry _telemetry;
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    private OrchestratorState _state = OrchestratorState.Init;

    /// <summary>
    /// Initializes a new instance of the <see cref="Orchestrator"/> class.
    /// </summary>
    /// <param name="monitorComponent">Service responsible for monitor management.</param>
    /// <param name="rotationComponent">Service responsible for rotation scheduling.</param>
    /// <param name="cycleComponent">Service responsible for cycle playback.</param>
    /// <param name="watchdogComponent">Service responsible for watchdog supervision.</param>
    /// <param name="telemetry">Telemetry sink used to record lifecycle events.</param>
    public Orchestrator(
        IOrchestrationComponent monitorComponent,
        IOrchestrationComponent rotationComponent,
        IOrchestrationComponent cycleComponent,
        IOrchestrationComponent watchdogComponent,
        ITelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(monitorComponent);
        ArgumentNullException.ThrowIfNull(rotationComponent);
        ArgumentNullException.ThrowIfNull(cycleComponent);
        ArgumentNullException.ThrowIfNull(watchdogComponent);
        ArgumentNullException.ThrowIfNull(telemetry);

        _telemetry = telemetry;
        _startupOrder = new[]
        {
            new ComponentRegistration("Monitors", monitorComponent),
            new ComponentRegistration("Rotation", rotationComponent),
            new ComponentRegistration("Cycle", cycleComponent),
            new ComponentRegistration("Watchdog", watchdogComponent),
        };
    }

    /// <summary>
    /// Gets the current state of the orchestrator.
    /// </summary>
    public OrchestratorState State => Volatile.Read(ref _state);

    /// <summary>
    /// Prepares the orchestrated services and transitions the orchestrator to the running state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _state);
            switch (current)
            {
                case OrchestratorState.Init:
                    TransitionTo(OrchestratorState.Ready);
                    await StartComponentsAsync(cancellationToken).ConfigureAwait(false);
                    TransitionTo(OrchestratorState.Running);
                    break;

                case OrchestratorState.Ready:
                    await StartComponentsAsync(cancellationToken).ConfigureAwait(false);
                    TransitionTo(OrchestratorState.Running);
                    break;

                case OrchestratorState.Running:
                    _telemetry.Info("Start requested while orchestrator is already running.");
                    break;

                case OrchestratorState.Recovering:
                    throw new InvalidOperationException("Cannot start the orchestrator while recovery is in progress.");

                default:
                    throw new InvalidOperationException($"Unsupported orchestrator state '{current}'.");
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// Stops all orchestrated services and returns the orchestrator to the ready state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _state);
            if (current is OrchestratorState.Init)
            {
                _telemetry.Info("Stop requested while orchestrator is in the initial state. No action taken.");
                return;
            }

            if (current is OrchestratorState.Ready)
            {
                _telemetry.Info("Stop requested while orchestrator is already idle.");
                return;
            }

            await StopComponentsAsync(cancellationToken).ConfigureAwait(false);
            TransitionTo(OrchestratorState.Ready);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// Attempts to recover orchestrated services after a failure.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _state) != OrchestratorState.Running)
            {
                _telemetry.Info("Recover requested while orchestrator is not running. Operation ignored.");
                return;
            }

            TransitionTo(OrchestratorState.Recovering);

            try
            {
                await RecoverComponentsAsync(cancellationToken).ConfigureAwait(false);
                TransitionTo(OrchestratorState.Running);
            }
            catch (OperationCanceledException)
            {
                TransitionTo(OrchestratorState.Running);
                throw;
            }
            catch (Exception)
            {
                await StopComponentsWithFallbackAsync().ConfigureAwait(false);
                TransitionTo(OrchestratorState.Ready);
                throw;
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task StartComponentsAsync(CancellationToken cancellationToken)
    {
        var started = new List<ComponentRegistration>();

        foreach (var component in _startupOrder)
        {
            try
            {
                await ExecuteAsync(
                        component,
                        "start",
                        static (instance, token) => instance.StartAsync(token),
                        cancellationToken)
                    .ConfigureAwait(false);

                started.Add(component);
            }
            catch
            {
                await RollbackStartAsync(started).ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task StopComponentsAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var component in _startupOrder.AsEnumerable().Reverse())
        {
            try
            {
                await ExecuteAsync(
                        component,
                        "stop",
                        static (instance, token) => instance.StopAsync(token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more orchestration components failed to stop.", failures);
        }
    }

    private async Task RecoverComponentsAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var component in _startupOrder)
        {
            try
            {
                await ExecuteAsync(
                        component,
                        "recover",
                        static (instance, token) => instance.RecoverAsync(token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more orchestration components failed to recover.", failures);
        }
    }

    private async Task ExecuteAsync(
        ComponentRegistration component,
        string operation,
        Func<IOrchestrationComponent, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        _telemetry.Info($"Executing '{operation}' for {component.Name} component.");

        try
        {
            await action(component.Component, cancellationToken).ConfigureAwait(false);
            _telemetry.Info($"Completed '{operation}' for {component.Name} component.");
        }
        catch (OperationCanceledException)
        {
            _telemetry.Warn($"'{operation}' for {component.Name} component was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _telemetry.Error($"'{operation}' for {component.Name} component failed.", exception);
            throw;
        }
    }

    private async Task RollbackStartAsync(IEnumerable<ComponentRegistration> startedComponents)
    {
        foreach (var component in startedComponents.Reverse())
        {
            try
            {
                _telemetry.Info($"Rolling back {component.Name} component due to start failure.");
                await component.Component.StopAsync(CancellationToken.None).ConfigureAwait(false);
                _telemetry.Info($"Rollback completed for {component.Name} component.");
            }
            catch (OperationCanceledException)
            {
                _telemetry.Warn($"Rollback for {component.Name} component was cancelled.");
            }
            catch (Exception exception)
            {
                _telemetry.Warn($"Rollback for {component.Name} component failed.", exception);
            }
        }
    }

    private async Task StopComponentsWithFallbackAsync()
    {
        try
        {
            await StopComponentsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _telemetry.Warn("Failed to stop components while handling recovery failure.", exception);
        }
    }

    private void TransitionTo(OrchestratorState next)
    {
        var previous = Volatile.Read(ref _state);
        if (previous == next)
        {
            return;
        }

        _telemetry.Info($"Orchestrator state changed from {previous} to {next}.");
        Volatile.Write(ref _state, next);
    }

    private sealed record class ComponentRegistration(string Name, IOrchestrationComponent Component);
}
