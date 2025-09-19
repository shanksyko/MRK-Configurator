using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Services;

namespace Mieruka.App.Tray;

/// <summary>
/// Provides a no-op implementation of <see cref="IOrchestrationComponent"/> used when a
/// subsystem is not available in the current environment.
/// </summary>
internal sealed class NullOrchestrationComponent : IOrchestrationComponent
{
    /// <summary>
    /// Gets the singleton instance of the component.
    /// </summary>
    public static NullOrchestrationComponent Instance { get; } = new();

    private NullOrchestrationComponent()
    {
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task RecoverAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
