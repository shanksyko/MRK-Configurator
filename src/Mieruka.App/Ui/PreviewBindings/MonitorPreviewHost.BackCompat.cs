using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Back-compat shim for legacy synchronous stop pipeline invocations.
/// Preserves binary compatibility while new orchestrator APIs are stabilized.
/// TODO: Replace with direct call to the definitive stop path once available.
/// </summary>
public sealed partial class MonitorPreviewHost
{
    private void StopCoreUnsafe(bool clearFrame, bool resetPaused)
    {
        // Back-compat: block on the async path to mimic legacy behavior.
        StopCoreUnsafeAsync(clearFrame, resetPaused, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }
}
