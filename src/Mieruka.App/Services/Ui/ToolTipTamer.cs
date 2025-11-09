using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Centralizes the configuration applied to every <see cref="ToolTip"/> used in the UI.
/// </summary>
public static class ToolTipTamer
{
    private const int DesiredInitialDelay = 400;
    private const int DesiredReshowDelay = 100;
    private const int DesiredAutoPopDelay = 8000;

    /// <summary>
    /// Creates a <see cref="ToolTip"/> with the standardized configuration.
    /// </summary>
    /// <param name="container">Optional container used to manage the component lifetime.</param>
    /// <returns>The configured tooltip instance.</returns>
    public static WinForms.ToolTip Create(IContainer? container = null)
    {
        var toolTip = container is null ? new WinForms.ToolTip() : new WinForms.ToolTip(container);
        return Configure(toolTip);
    }

    /// <summary>
    /// Applies the standardized configuration to the provided <see cref="ToolTip"/>.
    /// </summary>
    /// <param name="toolTip">Tooltip to configure.</param>
    /// <returns>The configured tooltip for chaining.</returns>
    public static WinForms.ToolTip Configure(WinForms.ToolTip toolTip)
    {
        ArgumentNullException.ThrowIfNull(toolTip);

        toolTip.InitialDelay = DesiredInitialDelay;
        toolTip.ReshowDelay = DesiredReshowDelay;
        toolTip.AutoPopDelay = DesiredAutoPopDelay;
        toolTip.ShowAlways = true;

        toolTip.Popup -= ToolTipOnPopup;
        toolTip.Popup += ToolTipOnPopup;

        return toolTip;
    }

    /// <summary>
    /// Applies the standardized configuration to every <see cref="ToolTip"/> contained in the provided container.
    /// </summary>
    /// <param name="container">Container that may include tooltips.</param>
    public static void Tame(IContainer? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var toolTip in container.Components.OfType<WinForms.ToolTip>())
        {
            Configure(toolTip);
        }
    }

    /// <summary>
    /// Applies the standardized configuration to tooltips attached to a form and
    /// disables flicker-prone tooltip effects on known hot-tracked controls.
    /// </summary>
    /// <param name="root">Root control whose descendants should be inspected.</param>
    /// <param name="container">Container associated with the control tree.</param>
    public static void Tame(WinForms.Control root, IContainer? container)
    {
        ArgumentNullException.ThrowIfNull(root);

        Tame(container);
        DisableHotTracking(root);
    }

    private static void ToolTipOnPopup(object? sender, PopupEventArgs e)
    {
        if (sender is not WinForms.ToolTip toolTip)
        {
            return;
        }

        if (IsFlickerProne(e.AssociatedControl))
        {
            toolTip.UseAnimation = false;
            toolTip.UseFading = false;
        }
        else
        {
            toolTip.UseAnimation = true;
            toolTip.UseFading = true;
        }
    }

    private static bool IsFlickerProne(WinForms.Control? control)
    {
        return control is WinForms.ListView or DataGridView;
    }

    private static void DisableHotTracking(WinForms.Control root)
    {
        foreach (var control in EnumerateSelfAndChildren(root))
        {
            switch (control)
            {
                case WinForms.ListView listView when listView.HotTracking:
                    listView.HotTracking = false;
                    break;
                case WinForms.ListView listView when listView.HoverSelection:
                    listView.HoverSelection = false;
                    break;
            }
        }
    }

    private static IEnumerable<WinForms.Control> EnumerateSelfAndChildren(WinForms.Control root)
    {
        var stack = new Stack<WinForms.Control>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            foreach (WinForms.Control child in current.Controls)
            {
                stack.Push(child);
            }
        }
    }
}
