using System;
using System.Windows.Forms;

namespace Mieruka.App.Ui
{
    internal static class SplitterGuards
    {
        public static void ForceSafeSplitter(SplitContainer s, int? desired = null)
        {
            if (s == null || s.IsDisposed) return;
            if (!s.IsHandleCreated || s.Width <= 0)
            {
                s.BeginInvoke(new Action(() => ForceSafeSplitter(s, desired)));
                return;
            }

            s.SuspendLayout();
            try
            {
                int origMin1 = s.Panel1MinSize;
                int origMin2 = s.Panel2MinSize;

                // alivio temporário para evitar range inválido
                s.Panel1MinSize = 0;
                s.Panel2MinSize = 0;

                int min = 0;
                int max = Math.Max(0, s.Width - s.SplitterWidth);

                int target = desired ?? (int)Math.Round(s.Width * 0.35);
                target = Math.Clamp(target, min, max);

                try
                {
                    s.SplitterDistance = target;
                }
                catch
                {
                    s.SplitterDistance = Math.Max(0, s.Width / 2);
                }

                // restaura minSizes e clamp final no range real
                s.Panel1MinSize = origMin1;
                s.Panel2MinSize = origMin2;

                int hardMin = s.Panel1MinSize;
                int hardMax = s.Width - s.Panel2MinSize - s.SplitterWidth;
                if (hardMax > hardMin)
                {
                    int cur = s.SplitterDistance;
                    int clamp = Math.Clamp(cur, hardMin, hardMax);
                    if (clamp != cur) s.SplitterDistance = clamp;
                }
            }
            finally
            {
                s.ResumeLayout();
            }
        }

        public static void WireSplitterGuards(SplitContainer s, int? desired = null)
        {
            s.HandleCreated += (_, __) => ForceSafeSplitter(s, desired);
            s.SizeChanged   += (_, __) => ForceSafeSplitter(s, desired);
            s.SplitterMoved += (_, __) => ForceSafeSplitter(s);

            if (s.FindForm() is Form f)
                f.DpiChanged += (_, __) => ForceSafeSplitter(s, desired);

            // garante execução após o primeiro layout estável
            s.BeginInvoke(new Action(() => ForceSafeSplitter(s, desired)));
        }
    }
}
