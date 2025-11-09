using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Services;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services;

/// <summary>
/// Windows Forms implementation of <see cref="IDialogHost"/>.
/// </summary>
public sealed class WinFormsDialogHost : IDialogHost
{
    private readonly WinForms.Control _owner;

    public WinFormsDialogHost(WinForms.Control owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (_owner.IsDisposed)
        {
            return Task.FromResult(false);
        }

        var tcs = new TaskCompletionSource<bool>();
        CancellationTokenRegistration registration = default;

        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            _ = tcs.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        void ShowDialog()
        {
            if (_owner.IsDisposed)
            {
                tcs.TrySetResult(false);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                var result = WinForms.MessageBox.Show(
                    _owner,
                    message,
                    title,
                    WinForms.MessageBoxButtons.YesNo,
                    WinForms.MessageBoxIcon.Question);

                tcs.TrySetResult(result == WinForms.DialogResult.Yes);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        try
        {
            if (_owner.InvokeRequired)
            {
                _owner.BeginInvoke((Action)ShowDialog);
            }
            else
            {
                ShowDialog();
            }
        }
        catch (ObjectDisposedException)
        {
            tcs.TrySetResult(false);
        }

        return tcs.Task;
    }
}
