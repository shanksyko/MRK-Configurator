using System;
using System.Windows.Forms;

namespace Mieruka.App.Ui;

internal static class LayoutGuards
{
    public static void SafeApplySplitter(SplitContainer container, int? desired = null)
    {
        if (container is null || container.IsDisposed)
        {
            return;
        }

        if (!container.IsHandleCreated)
        {
            try
            {
                container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            }
            catch (InvalidOperationException)
            {
                // Control is not ready to receive invoke requests. The guard will run
                // again once the container is created or sized.
            }
            catch (ObjectDisposedException)
            {
                // Control is disposing; nothing to apply.
            }

            return;
        }

        var displaySize = container.Orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;

        if (displaySize <= 0)
        {
            return;
        }

        var splitterWidth = Math.Max(0, container.SplitterWidth);
        var panel1Min = Math.Max(0, container.Panel1MinSize);
        var panel2Min = Math.Max(0, container.Panel2MinSize);
        var available = displaySize - splitterWidth;

        if (available <= 0)
        {
            return;
        }

        var max = available - panel2Min;
        if (max < panel1Min)
        {
            return;
        }

        var fallback = displaySize / 2;
        var target = desired ?? fallback;
        var clamped = Math.Clamp(target, panel1Min, max);

        try
        {
            container.SplitterDistance = clamped;
        }
        catch (ArgumentException)
        {
            // If the container cannot satisfy the layout constraints at the current size,
            // keep the existing SplitterDistance. The SizeChanged guard will run again
            // after layout and attempt to apply the clamp with updated bounds.
        }
        catch (InvalidOperationException)
        {
            container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
        }
    }

    public static void WireSplitterGuards(SplitContainer container, int? desired = null)
    {
        if (container is null)
        {
            return;
        }

        void ApplySafeSplitter() => SafeApplySplitter(container, desired);
        void ApplyClampOnly() => SafeApplySplitter(container);

        container.HandleCreated += (_, _) => ApplySafeSplitter();
        container.SizeChanged += (_, _) => ApplySafeSplitter();
        container.SplitterMoved += (_, _) => ApplyClampOnly();

        Form? trackedForm = null;
        DpiChangedEventHandler dpiHandler = (_, _) => SafeApplySplitter(container, desired);

        void AttachFormHandler()
        {
            var form = container.FindForm();
            if (ReferenceEquals(form, trackedForm))
            {
                return;
            }

            if (trackedForm is not null)
            {
                trackedForm.DpiChanged -= dpiHandler;
            }

            trackedForm = form;
            if (trackedForm is not null)
            {
                trackedForm.DpiChanged += dpiHandler;
            }
        }

        container.ParentChanged += (_, _) => AttachFormHandler();
        container.Disposed += (_, _) =>
        {
            if (trackedForm is not null)
            {
                trackedForm.DpiChanged -= dpiHandler;
                trackedForm = null;
            }
        };

        AttachFormHandler();

        if (container.IsHandleCreated)
        {
            ApplySafeSplitter();
        }
    }
}
