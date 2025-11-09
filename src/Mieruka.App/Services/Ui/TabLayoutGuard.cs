#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Suspends the layout of the parent container while <see cref="TabControl"/> instances
/// switch tabs in order to avoid expensive intermediate layout calculations.
/// </summary>
internal static class TabLayoutGuard
{
    private sealed class TabState
    {
        public bool LayoutSuspended;
        public WinForms.Control? Container;
    }

    private sealed class ControlRegistration
    {
    }

    private static readonly ConditionalWeakTable<TabControl, TabState> TabStates = new();
    private static readonly ConditionalWeakTable<WinForms.Control, ControlRegistration> ControlRegistrations = new();

    /// <summary>
    /// Observes the specified control tree and attaches layout guards to any
    /// <see cref="TabControl"/> instances that are found.
    /// </summary>
    /// <param name="root">Root control that contains tab controls.</param>
    public static void Attach(WinForms.Control root)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        AttachRecursive(root);
    }

    private static void AttachRecursive(WinForms.Control control)
    {
        if (control is TabControl tabControl)
        {
            Attach(tabControl);
        }

        if (!ControlRegistrations.TryGetValue(control, out _))
        {
            ControlRegistrations.Add(control, new ControlRegistration());
            control.ControlAdded += ControlOnControlAdded;
            control.Disposed += ControlOnDisposed;
        }

        foreach (WinForms.Control child in control.Controls)
        {
            AttachRecursive(child);
        }
    }

    private static void ControlOnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is null)
        {
            return;
        }

        AttachRecursive(e.Control);
    }

    private static void ControlOnDisposed(object? sender, EventArgs e)
    {
        if (sender is WinForms.Control control)
        {
            control.ControlAdded -= ControlOnControlAdded;
            control.Disposed -= ControlOnDisposed;
            ControlRegistrations.Remove(control);
        }
    }

    private static void Attach(TabControl tabControl)
    {
        if (TabStates.TryGetValue(tabControl, out _))
        {
            return;
        }

        TabStates.Add(tabControl, new TabState());
        tabControl.Selecting += OnSelecting;
        tabControl.Selected += OnSelected;
        tabControl.Disposed += OnTabDisposed;
    }

    private static void OnTabDisposed(object? sender, EventArgs e)
    {
        if (sender is not TabControl tabControl)
        {
            return;
        }

        if (TabStates.TryGetValue(tabControl, out var state))
        {
            ResumeLayout(state);
        }

        tabControl.Selecting -= OnSelecting;
        tabControl.Selected -= OnSelected;
        tabControl.Disposed -= OnTabDisposed;

        TabStates.Remove(tabControl);
    }

    private static void OnSelecting(object? sender, TabControlCancelEventArgs e)
    {
        if (sender is not TabControl tabControl)
        {
            return;
        }

        if (!TabStates.TryGetValue(tabControl, out var state))
        {
            return;
        }

        if (state.LayoutSuspended)
        {
            return;
        }

        var container = tabControl.Parent;
        if (container is null)
        {
            return;
        }

        container.SuspendLayout();
        state.LayoutSuspended = true;
        state.Container = container;

        if (tabControl.IsHandleCreated)
        {
            tabControl.BeginInvoke(new Action(() => ResumeLayout(tabControl)));
        }

        if (e.Cancel)
        {
            ResumeLayout(state);
        }
    }

    private static void OnSelected(object? sender, TabControlEventArgs e)
    {
        if (sender is not TabControl tabControl)
        {
            return;
        }

        if (!TabStates.TryGetValue(tabControl, out var state))
        {
            return;
        }

        ResumeLayout(state);
    }

    private static void ResumeLayout(TabControl tabControl)
    {
        if (!TabStates.TryGetValue(tabControl, out var state))
        {
            return;
        }

        ResumeLayout(state);
    }

    private static void ResumeLayout(TabState state)
    {
        if (!state.LayoutSuspended)
        {
            return;
        }

        var container = state.Container;
        state.LayoutSuspended = false;
        state.Container = null;
        container?.ResumeLayout(true);
    }
}
