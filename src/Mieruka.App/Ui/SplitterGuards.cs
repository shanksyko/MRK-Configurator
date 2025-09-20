using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Mieruka.App.Ui
{
    internal static class SplitterGuards
    {
        private sealed class MinSizes
        {
            public MinSizes(int panel1, int panel2)
            {
                Panel1 = panel1;
                Panel2 = panel2;
            }

            public int Panel1 { get; }
            public int Panel2 { get; }
        }

        private static readonly ConditionalWeakTable<SplitContainer, MinSizes> OriginalMinSizes = new();

        private static MinSizes GetOriginalMinSizes(SplitContainer container)
        {
            return OriginalMinSizes.GetValue(container, key => new MinSizes(key.Panel1MinSize, key.Panel2MinSize));
        }

        public static void ForceSafeSplitter(SplitContainer s, int? desired = null)
        {
            if (s == null || s.IsDisposed) return;

            bool NeedsRetry()
            {
                if (!s.IsHandleCreated) return true;

                return s.Orientation == Orientation.Horizontal
                    ? s.ClientSize.Height <= 0
                    : s.ClientSize.Width <= 0;
            }

            if (NeedsRetry())
            {
                s.BeginInvoke(new Action(() => ForceSafeSplitter(s, desired)));
                return;
            }

            s.SuspendLayout();
            try
            {
                var defaults = GetOriginalMinSizes(s);

                int length = s.Orientation == Orientation.Horizontal
                    ? s.ClientSize.Height
                    : s.ClientSize.Width;

                int span = Math.Max(0, length - s.SplitterWidth);
                int target = desired ?? (int)Math.Round(length * 0.35);
                target = Math.Clamp(target, 0, span);

                int effectiveMin1 = Math.Clamp(defaults.Panel1, 0, span);
                int effectiveMin2 = Math.Clamp(defaults.Panel2, 0, span);

                if (effectiveMin1 + effectiveMin2 > span)
                {
                    int overflow = effectiveMin1 + effectiveMin2 - span;
                    if (effectiveMin2 >= effectiveMin1)
                    {
                        effectiveMin2 = Math.Max(0, effectiveMin2 - overflow);
                    }
                    else
                    {
                        effectiveMin1 = Math.Max(0, effectiveMin1 - overflow);
                    }
                }

                int maxDistance = Math.Max(effectiveMin1, span - effectiveMin2);
                int safeDistance = Math.Clamp(target, effectiveMin1, maxDistance);

                s.Panel1MinSize = effectiveMin1;
                s.Panel2MinSize = effectiveMin2;

                try
                {
                    if (s.SplitterDistance != safeDistance)
                    {
                        s.SplitterDistance = safeDistance;
                    }
                }
                catch
                {
                    int fallback = Math.Clamp(span / 2, effectiveMin1, maxDistance);
                    s.SplitterDistance = fallback;
                }

                if (span >= defaults.Panel1 + defaults.Panel2)
                {
                    bool restored = false;

                    if (s.Panel1MinSize != defaults.Panel1)
                    {
                        s.Panel1MinSize = defaults.Panel1;
                        restored = true;
                    }

                    if (s.Panel2MinSize != defaults.Panel2)
                    {
                        s.Panel2MinSize = defaults.Panel2;
                        restored = true;
                    }

                    if (restored)
                    {
                        int hardMin = s.Panel1MinSize;
                        int hardMax = Math.Max(hardMin, span - s.Panel2MinSize);
                        int current = s.SplitterDistance;
                        int clamp = Math.Clamp(current, hardMin, hardMax);
                        if (clamp != current)
                        {
                            s.SplitterDistance = clamp;
                        }
                    }
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
