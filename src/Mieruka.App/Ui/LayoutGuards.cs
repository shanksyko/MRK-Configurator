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
            container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            return;
        }

        var clientSize = container.ClientSize;
        var orientation = container.Orientation;
        var totalLength = orientation == Orientation.Horizontal
            ? clientSize.Height
            : clientSize.Width;
        var splitterThickness = Math.Max(0, container.SplitterWidth);
        var availableLength = Math.Max(0, totalLength - splitterThickness);

        if (availableLength <= 0)
        {
            container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            return;
        }

        var panel1Min = Math.Max(0, container.Panel1MinSize);
        var panel2Min = Math.Max(0, container.Panel2MinSize);

        var minDistance = Math.Min(panel1Min, availableLength);
        var maxDistance = Math.Max(availableLength - panel2Min, minDistance);
        maxDistance = Math.Clamp(maxDistance, minDistance, availableLength);

        var current = desired ?? container.SplitterDistance;
        var clamped = Math.Clamp(current, minDistance, maxDistance);

        if (clamped == container.SplitterDistance)
        {
            return;
        }

        try
        {
            container.SplitterDistance = clamped;
        }
        catch (ArgumentException)
        {
            var fallback = Math.Clamp(availableLength / 2, minDistance, maxDistance);
            if (fallback != container.SplitterDistance)
            {
                try
                {
                    container.SplitterDistance = fallback;
                }
                catch (ArgumentException)
                {
                    // If both panels enforce incompatible minimum sizes, keep the previous distance.
                }
            }
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
        else
        {
            container.BeginInvoke(new Action(ApplySafeSplitter));
        }
    }
}
