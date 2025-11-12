using System.Windows.Forms;

namespace Mieruka.App.Forms;

/// <summary>
/// Back-compat shim for legacy synchronous monitor test window lifecycle.
/// Keeps historical callers compiling until dedicated context APIs are restored.
/// TODO: Remove once all code paths use the async CloseTestWindowAsync variant.
/// </summary>
public partial class MainForm : Form
{
    private sealed partial class MonitorCardContext
    {
        private void CloseTestWindow()
        {
            // Back-compat: fire-and-forget close matching legacy synchronous expectations.
            if (TestWindow is not { IsDisposed: false } window)
            {
                TestWindow = null;
                _ = ResumeHostFromTestWindowAsync();
                return;
            }

            TestWindow = null;
            window.FormClosed -= OnTestWindowClosed;

            try
            {
                window.Close();
            }
            catch
            {
                // Swallow legacy close failures.
            }

            _ = ResumeHostFromTestWindowAsync();
        }
    }
}
