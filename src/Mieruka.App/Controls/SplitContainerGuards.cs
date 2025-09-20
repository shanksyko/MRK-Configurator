using System;
using System.Windows.Forms;

namespace Mieruka.App.Controls;

/// <summary>
/// Provides helper methods that keep <see cref="SplitContainer"/> instances in a safe state.
/// </summary>
internal static class SplitContainerGuards
{
    /// <summary>
    /// Applies a splitter distance ensuring that panel minimum sizes are honoured.
    /// </summary>
    /// <param name="container">Container to adjust.</param>
    /// <param name="desired">Optional desired splitter distance in pixels.</param>
    public static void ForceSafeSplitter(SplitContainer? container, int? desired = null)
    {
        if (container is null)
        {
            return;
        }

        var available = container.Orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;

        if (available <= 0)
        {
            return;
        }

        var originalPanel1Min = container.Panel1MinSize;
        var originalPanel2Min = container.Panel2MinSize;

        var minimum = Math.Max(0, originalPanel1Min);
        var maximum = Math.Max(minimum, available - Math.Max(0, originalPanel2Min));

        var target = desired ?? container.SplitterDistance;
        target = Math.Clamp(target, minimum, maximum);

        if (container.SplitterDistance == target)
        {
            return;
        }

        container.SuspendLayout();
        try
        {
            container.Panel1MinSize = 0;
            container.Panel2MinSize = 0;

            try
            {
                container.SplitterDistance = target;
            }
            catch (ArgumentOutOfRangeException)
            {
                container.SplitterDistance = Math.Clamp(target, minimum, maximum);
            }
        }
        finally
        {
            container.Panel1MinSize = originalPanel1Min;
            container.Panel2MinSize = originalPanel2Min;

            var refreshedAvailable = container.Orientation == Orientation.Horizontal
                ? container.ClientSize.Height
                : container.ClientSize.Width;
            var refreshedMaximum = Math.Max(container.Panel1MinSize, refreshedAvailable - container.Panel2MinSize);
            container.SplitterDistance = Math.Clamp(container.SplitterDistance, container.Panel1MinSize, refreshedMaximum);
            container.ResumeLayout();
        }
    }

    /// <summary>
    /// Wires event handlers that keep the splitter distance within safe bounds.
    /// </summary>
    /// <param name="container">Container that should be guarded.</param>
    /// <param name="desired">Optional desired splitter distance in pixels.</param>
    public static void WireSplitterGuards(SplitContainer? container, int? desired = null)
    {
        if (container is null)
        {
            return;
        }

        void Apply()
        {
            ForceSafeSplitter(container, desired);
        }

        container.HandleCreated += (_, _) => container.BeginInvoke(new MethodInvoker(Apply));
        container.SizeChanged += (_, _) => container.BeginInvoke(new MethodInvoker(Apply));
        container.SplitterMoved += (_, _) => container.BeginInvoke(new MethodInvoker(Apply));
        container.DpiChangedAfterParent += (_, _) => container.BeginInvoke(new MethodInvoker(Apply));

        if (container.IsHandleCreated)
        {
            container.BeginInvoke(new MethodInvoker(Apply));
        }
    }
}
