using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.Core.Contracts;

/// <summary>
/// Coordinates temporary suspension of WinForms binding pipelines while complex UI updates are performed.
/// </summary>
public interface IBindingService
{
    /// <summary>
    /// Tracks a <see cref="BindingSource"/> so it participates in batch suspensions.
    /// </summary>
    void Track(BindingSource source);

    /// <summary>
    /// Tracks a <see cref="BindingList{T}"/> so it participates in batch suspensions.
    /// </summary>
    /// <typeparam name="T">Type of the elements contained in the binding list.</typeparam>
    void Track<T>(BindingList<T> list);

    /// <summary>
    /// Suspends change notifications for tracked bindings until the returned scope is disposed.
    /// </summary>
    IDisposable BeginBatch();
}
