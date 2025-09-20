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

        if (container.Panel1Collapsed || container.Panel2Collapsed)
        {
            return;
        }

        if (!container.IsHandleCreated)
        {
            try
            {
                container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            }
            catch (ObjectDisposedException)
            {
                // Control is disposing; nothing to apply.
            }
            catch (InvalidOperationException)
            {
                // Control is not ready to receive invoke requests. The guard will run
                // again once the container is created or sized.
            }

            return;
        }

        var extent = container.Orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;

        if (extent <= 0)
        {
            return;
        }

        var splitterWidth = Math.Max(0, container.SplitterWidth);
        var panel1Min = Math.Max(0, container.Panel1MinSize);
        var panel2Min = Math.Max(0, container.Panel2MinSize);
        var available = Math.Max(0, extent - splitterWidth);

        if (available <= 0)
        {
            return;
        }

        panel1Min = Math.Min(panel1Min, available);
        panel2Min = Math.Min(panel2Min, available);

        var max = available - panel2Min;
        if (max < panel1Min)
        {
            return;
        }

        var fallback = Math.Clamp(available / 2, panel1Min, max);
        var target = desired ?? fallback;
        var clamped = Math.Clamp(target, panel1Min, max);

        if (container.SplitterDistance == clamped)
        {
            return;
        }

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

        int? preferred = desired;

        void ApplySafeSplitter()
        {
            SafeApplySplitter(container, preferred);
            preferred = container.IsHandleCreated ? container.SplitterDistance : preferred;
        }

        void ApplyClampOnly()
        {
            preferred = container.IsHandleCreated ? container.SplitterDistance : preferred;
            SafeApplySplitter(container, preferred);
        }

        container.SplitterMoved += (_, _) => ApplyClampOnly();

        Form? trackedForm = null;
        EventHandler formCreatedHandler = (_, _) => ApplySafeSplitter();
        EventHandler formSizedHandler = (_, _) => ApplySafeSplitter();
        DpiChangedEventHandler dpiHandler = (_, _) => ApplySafeSplitter();

        void AttachFormHandler()
        {
            var form = container.FindForm();
            if (ReferenceEquals(form, trackedForm))
            {
                return;
            }

            if (trackedForm is not null)
            {
                trackedForm.HandleCreated -= formCreatedHandler;
                trackedForm.SizeChanged -= formSizedHandler;
                trackedForm.DpiChanged -= dpiHandler;
            }

            trackedForm = form;
            if (trackedForm is not null)
            {
                trackedForm.HandleCreated += formCreatedHandler;
                trackedForm.SizeChanged += formSizedHandler;
                trackedForm.DpiChanged += dpiHandler;

                if (trackedForm.IsHandleCreated)
                {
                    ApplySafeSplitter();
                }
            }
        }

        container.ParentChanged += (_, _) => AttachFormHandler();
        container.Disposed += (_, _) =>
        {
            if (trackedForm is not null)
            {
                trackedForm.HandleCreated -= formCreatedHandler;
                trackedForm.SizeChanged -= formSizedHandler;
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
