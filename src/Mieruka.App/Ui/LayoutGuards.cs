using System;
using System.Windows.Forms;
using Serilog;

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
        var target = desired ?? (container.IsHandleCreated ? container.SplitterDistance : fallback);

        if (!desired.HasValue && container.IsHandleCreated)
        {
            if (target < panel1Min || target > max)
            {
                Log.Warning(
                    "Detected invalid SplitterDistance {SplitterDistance} for {Context}; resetting to fallback {Fallback}.",
                    container.SplitterDistance,
                    GetContext(container),
                    fallback);
                target = fallback;
            }
        }

        var clamped = Math.Clamp(target, panel1Min, max);

        if (container.SplitterDistance == clamped)
        {
            return;
        }

        try
        {
            container.SplitterDistance = clamped;
        }
        catch (ArgumentException ex)
        {
            Log.Warning(
                ex,
                "Failed to apply splitter distance {SplitterDistance} to {Context}. Current bounds will be retried on resize.",
                clamped,
                GetContext(container));
        }
        catch (InvalidOperationException ex)
        {
            Log.Debug(
                ex,
                "Deferring splitter update for {Context} until handle is ready.",
                GetContext(container));

            try
            {
                container.BeginInvoke(new Action(() => SafeApplySplitter(container, desired)));
            }
            catch (ObjectDisposedException)
            {
                // Control disposed between attempts; nothing more to do.
            }
            catch (InvalidOperationException)
            {
                // Control not ready for invoke yet; a subsequent layout cycle will trigger another attempt.
            }
        }
    }

    public static void SetSafeMinSizes(SplitContainer? container, int panel1Min, int panel2Min)
    {
        if (container is null || container.IsDisposed)
        {
            return;
        }

        panel1Min = Math.Max(0, panel1Min);
        panel2Min = Math.Max(0, panel2Min);

        var orientation = container.Orientation;
        var extent = orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;
        var splitterWidth = Math.Max(0, container.SplitterWidth);
        var available = Math.Max(0, extent - splitterWidth);

        if (available > 0)
        {
            var total = panel1Min + panel2Min;
            if (total > available && total > 0)
            {
                var scale = available / (double)total;
                var scaledPanel1 = (int)Math.Round(panel1Min * scale, MidpointRounding.AwayFromZero);
                scaledPanel1 = Math.Clamp(scaledPanel1, 0, available);
                var scaledPanel2 = available - scaledPanel1;

                panel1Min = scaledPanel1;
                panel2Min = Math.Max(0, scaledPanel2);
            }
        }

        container.Panel1MinSize = panel1Min;
        container.Panel2MinSize = panel2Min;

        var desired = container.IsHandleCreated ? container.SplitterDistance : (int?)null;
        SafeApplySplitter(container, desired);
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
        container.SizeChanged += (_, _) => ApplySafeSplitter();

        Form? trackedForm = null;
        EventHandler formCreatedHandler = (_, _) => ApplySafeSplitter();
        EventHandler formSizedHandler = (_, _) => ApplySafeSplitter();
        DpiChangedEventHandler? dpiHandler = null;
        var supportsPerMonitorDpi = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
        if (supportsPerMonitorDpi)
        {
            dpiHandler = (_, _) => ApplySafeSplitter();
        }

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
                if (dpiHandler is not null)
                {
                    trackedForm.DpiChanged -= dpiHandler;
                }
            }

            trackedForm = form;
            if (trackedForm is not null)
            {
                trackedForm.HandleCreated += formCreatedHandler;
                trackedForm.SizeChanged += formSizedHandler;
                if (dpiHandler is not null)
                {
                    trackedForm.DpiChanged += dpiHandler;
                }

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
                if (dpiHandler is not null)
                {
                    trackedForm.DpiChanged -= dpiHandler;
                }
                trackedForm = null;
            }
        };

        AttachFormHandler();

        if (container.IsHandleCreated)
        {
            ApplySafeSplitter();
        }
    }

    private static string GetContext(SplitContainer container)
    {
        if (!string.IsNullOrWhiteSpace(container.Name))
        {
            return container.Name;
        }

        var accessibleName = container.AccessibleName;
        if (!string.IsNullOrWhiteSpace(accessibleName))
        {
            return accessibleName;
        }

        return container.GetType().Name;
    }
}
