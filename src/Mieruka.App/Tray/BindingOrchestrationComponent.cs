using System;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Services;
using Mieruka.Core.Services;

namespace Mieruka.App.Tray;

/// <summary>
/// Wraps the <see cref="BindingTrayService"/> so it can participate in the orchestrator lifecycle.
/// </summary>
internal sealed class BindingOrchestrationComponent : IOrchestrationComponent
{
    private readonly BindingTrayService _bindingService;
    private readonly ITelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingOrchestrationComponent"/> class.
    /// </summary>
    /// <param name="bindingService">Service responsible for applying window bindings.</param>
    /// <param name="telemetry">Telemetry sink used to record lifecycle events.</param>
    public BindingOrchestrationComponent(BindingTrayService bindingService, ITelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(bindingService);
        ArgumentNullException.ThrowIfNull(telemetry);

        _bindingService = bindingService;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _telemetry.Info("Reapplying window bindings as part of orchestrator start.");
        _bindingService.ReapplyAllBindings();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        _telemetry.Info("Reapplying window bindings as part of orchestrator recovery.");
        _bindingService.ReapplyAllBindings();
        return Task.CompletedTask;
    }
}
