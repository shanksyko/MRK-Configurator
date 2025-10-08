using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.App.Simulation;

/// <summary>
/// Provides helper routines used to simulate cycle playback inside the configurator.
/// </summary>
internal sealed class AppCycleSimulator
{
    /// <summary>
    /// Gets the default delay applied when items omit their explicit duration, in milliseconds.
    /// </summary>
    public const int DefaultDelayMs = 200;

    /// <summary>
    /// Represents an item that can be simulated inside the cycle preview.
    /// </summary>
    /// <param name="Key">Unique identifier of the simulated rectangle.</param>
    /// <param name="DisplayName">Friendly display name associated with the item.</param>
    /// <param name="RequiresNetwork">Indicates whether the simulation depends on network availability.</param>
    /// <param name="DelayMs">Optional delay applied to the item in milliseconds.</param>
    /// <param name="Details">Additional details shown in tooltips.</param>
    public sealed record class SimRect(
        string Key,
        string DisplayName,
        bool RequiresNetwork,
        int? DelayMs = null,
        string? Details = null);

    /// <summary>
    /// Iterates through the supplied items, applying the provided callbacks for each simulated step.
    /// </summary>
    /// <param name="items">Collection of rectangles that should be simulated in order.</param>
    /// <param name="isNetworkAvailable">Delegate used to determine whether the network is currently reachable.</param>
    /// <param name="onStart">Callback executed right before an item becomes active.</param>
    /// <param name="onEnd">Callback executed after the item finishes its simulated duration.</param>
    /// <param name="cancellationToken">Token used to cancel the simulation.</param>
    public async Task SimulateAsync(
        IReadOnlyList<SimRect> items,
        Func<bool> isNetworkAvailable,
        Action<SimRect>? onStart = null,
        Action<SimRect>? onEnd = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isNetworkAvailable);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.RequiresNetwork && !isNetworkAvailable())
            {
                continue;
            }

            onStart?.Invoke(item);

            try
            {
                var delay = item.DelayMs ?? DefaultDelayMs;
                if (delay < 0)
                {
                    delay = DefaultDelayMs;
                }

                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                onEnd?.Invoke(item);
            }
        }
    }
}
