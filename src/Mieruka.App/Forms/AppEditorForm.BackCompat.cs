using System.Windows.Forms;

namespace Mieruka.App.Forms;

/// <summary>
/// Back-compat shim bridging legacy synchronous preview suspension calls.
/// Ensures historical callers compile while orchestration APIs settle.
/// TODO: Remove once all entry points rely on SuspendPreviewCaptureAsync.
/// </summary>
public partial class AppEditorForm : Form
{
    private void SuspendPreviewCapture()
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            _ = SuspendPreviewCaptureAsync();
        }
        catch
        {
            // Back-compat: ignore errors raised while scheduling suspension.
        }
    }
}
