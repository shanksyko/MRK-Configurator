using System;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Mieruka.App.Ui;

[SupportedOSPlatform("windows10.0.17763")]
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
        var availableLength = container.Orientation == Orientation.Horizontal
            ? clientSize.Height
            : clientSize.Width;

        if (availableLength <= 0)
        {
            container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            return;
        }

        var panel1Min = Math.Max(0, container.Panel1MinSize);
        var panel2Min = Math.Max(0, container.Panel2MinSize);
        var maxDistance = Math.Max(panel1Min, availableLength - panel2Min);

        var current = desired ?? container.SplitterDistance;
        var clamped = Math.Clamp(current, panel1Min, maxDistance);

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
            var fallback = Math.Clamp(availableLength / 2, panel1Min, maxDistance);
            if (fallback != container.SplitterDistance)
            {
                container.SplitterDistance = fallback;
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

        DpiChangedEventHandler dpiHandler = (_, _) => ApplySafeSplitter();
        container.DpiChanged += dpiHandler;

        Form? trackedForm = null;

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
