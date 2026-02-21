#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.Core.Config;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Preview;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

public sealed partial class MonitorPreviewHost
{
    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        using var guard = new StackGuard(nameof(OnFrameArrived));
        if (!guard.Entered)
        {
            e.Dispose();
            return;
        }

        if (_suppressEvents)
        {
            e.Dispose();
            return;
        }

        if (ReadState() == PreviewState.Disposing)
        {
            e.Dispose();
            return;
        }

        if (Interlocked.Exchange(ref _frameCallbackGate, 1) != 0)
        {
            Interlocked.Increment(ref _reentrancyBlockedCount);
            MaybeReportGateBlocks();
            e.Dispose();
            return;
        }

        try
        {
            if (Volatile.Read(ref _paused) == 1)
            {
                _logger.Debug("FrameDroppedPaused");
                e.Dispose();
                return;
            }

            if (IsEditorPreviewDisabled)
            {
                _logger.Debug(
                    "[DBG] EditorPreviewDisabled: descartando frame de preview ao vivo monitor={MonitorId}",
                    MonitorId);
                e.Dispose();
                return;
            }

            if (IsEditorSnapshotActive)
            {
                _logger.Debug("EditorSnapshot: quadro ignorado porque snapshot está ativo");
                e.Dispose();
                return;
            }

            if (IsBusy || !ShouldDisplayFrame())
            {
                e.Dispose();
                return;
            }

            if (!ShouldProcessFrame())
            {
                e.Dispose();
                return;
            }

            Drawing.Bitmap? clone = null;
            try
            {
                var bitmap = e.Frame;
                if (!TryGetFrameSize(bitmap, out var width, out var height))
                {
                    return;
                }

                if (width <= 0 || height <= 0)
                {
                    ForEvent("FrameDiscarded").Debug(
                        "Quadro de pré-visualização descartado por dimensões inválidas {Width}x{Height}.",
                        width,
                        height);
                    return;
                }

                clone = TryCloneFrame(bitmap, width, height);
#if DEBUG
                if (clone is not null)
                {
                    DrawDebugOverlay(clone);
                }
#endif
            }
            finally
            {
                e.Dispose();
            }

            if (clone is null)
            {
                return;
            }

            RegisterPendingFrame(clone, out _);
            UpdateTarget(clone);
        }
        finally
        {
            Interlocked.Exchange(ref _frameCallbackGate, 0);
        }
    }

    private void MaybeReportGateBlocks()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastGateReportUtc).TotalSeconds < 5)
        {
            return;
        }

        var blocked = Interlocked.Exchange(ref _reentrancyBlockedCount, 0);
        if (blocked > 0)
        {
            _logger.Warning("Reentrancy blocked events={Count}", blocked);
        }

        _lastGateReportUtc = now;
    }

    private bool IsPlaceholderImage(Drawing.Image? image)
        => image is not null && ReferenceEquals(image, _previewPlaceholderBitmap);

    private Drawing.Size GetPlaceholderTargetSize()
    {
        try
        {
            var size = _target.ClientSize;
            if (size.Width > 0 && size.Height > 0)
            {
                return size;
            }
        }
        catch
        {
            // Ignore size retrieval failures; fall back to a default placeholder size.
        }

        return new Drawing.Size(800, 450);
    }

    private void EnsurePlaceholderBitmap(Drawing.Size targetSize)
    {
        var width = Math.Max(1, targetSize.Width);
        var height = Math.Max(1, targetSize.Height);

        if (_previewPlaceholderBitmap is { } existing
            && existing.Width == width
            && existing.Height == height)
        {
            return;
        }

        var bitmap = new Drawing.Bitmap(width, height);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(Drawing.Color.FromArgb(30, 30, 30));

            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(60, 60, 60));
            var step = Math.Max(20, Math.Min(width, height) / 10);
            for (var x = 0; x < width; x += step)
            {
                g.DrawLine(pen, x, 0, x, height);
            }

            for (var y = 0; y < height; y += step)
            {
                g.DrawLine(pen, 0, y, width, y);
            }

            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(120, 200, 200));
            using var font = new Drawing.Font(
                "Segoe UI",
                (float)Math.Max(10, Math.Min(width, height) / 24f),
                Drawing.FontStyle.Bold,
                Drawing.GraphicsUnit.Pixel);
            var text = "PREVIEW DESATIVADO";
            var textSize = g.MeasureString(text, font);
            var origin = new Drawing.PointF(
                (width - textSize.Width) / 2f,
                (height - textSize.Height) / 2f);
            g.DrawString(text, font, brush, origin);
        }

        var previous = Interlocked.Exchange(ref _previewPlaceholderBitmap, bitmap);
        if (previous is not null && !ReferenceEquals(previous, bitmap))
        {
            try
            {
                previous.Dispose();
            }
            catch
            {
            }
        }
    }

    // Pool de bitmaps reutilizáveis para evitar ~8MB de alocação por frame.
    // Ring buffer com 4 slots -" allows producer to write to one while consumer displays another.
    private readonly Drawing.Bitmap?[] _bitmapPool = new Drawing.Bitmap?[4];
    private int _bitmapPoolIndex;

    private Drawing.Bitmap? TryCloneFrame(Drawing.Image frame, int width, int height)
    {
        try
        {
            var poolIndex = _bitmapPoolIndex % _bitmapPool.Length;
            var reusable = _bitmapPool[poolIndex];

            // Reuse existing bitmap if dimensions match; otherwise allocate a new one.
            if (reusable is null || reusable.Width != width || reusable.Height != height)
            {
                // Don't dispose the old bitmap here -" it may still be referenced
                // by the PictureBox. The old image is disposed when replaced via
                // TryApplyFrame -> DisposeFrame chain.
                reusable = new Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                _bitmapPool[poolIndex] = reusable;
            }

            using (var g = Drawing.Graphics.FromImage(reusable))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(frame, 0, 0, width, height);
            }

            _bitmapPoolIndex = poolIndex + 1;

            // Return the pool bitmap directly instead of cloning.
            // The ring buffer has enough slots to ensure the PictureBox
            // still holds a valid previous frame while we write the next one.
            return reusable;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            ForEvent("FrameDiscarded").Debug(
                ex,
                "Quadro de pré-visualização descartado por falha ao clonar frame {Width}x{Height}.",
                width,
                height);
            return null;
        }
        catch (Exception ex) when (ex is OutOfMemoryException)
        {
            ForEvent("FrameDiscarded").Warning(
                ex,
                "Quadro de pré-visualização descartado por falta de memória ao clonar frame {Width}x{Height}.",
                width,
                height);
            return null;
        }
    }

    private bool TryApplyFrame(Drawing.Bitmap frame)
    {
        if (!TryGetFrameSize(frame, out var width, out var height))
        {
            frame.Dispose();
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            ForEvent("FrameDiscarded").Debug(
                "Quadro de pré-visualização descartado por dimensões inválidas {Width}x{Height}.",
                width,
                height);
            frame.Dispose();
            return false;
        }

        var previous = Interlocked.Exchange(ref _currentFrame, frame);
        var animationWasStopped = false;

        if (previous is not null)
        {
            animationWasStopped = StopAnimationSafe(previous);
        }

        try
        {
            _target.Image = frame;
            StartAnimationSafe(frame);
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            ForEvent("FrameDiscarded").Debug(ex, "Quadro de pré-visualização descartado por imagem inválida.");
            StopAnimationSafe(frame);
            frame.Dispose();

            if (previous is not null)
            {
                if (animationWasStopped)
                {
                    StartAnimationSafe(previous);
                }

                Interlocked.Exchange(ref _currentFrame, previous);
                _target.Image = previous;
            }
            else
            {
                Interlocked.Exchange(ref _currentFrame, null);
                _target.Image = null;
            }

            return false;
        }
        finally
        {
            if (previous is not null && !ReferenceEquals(previous, _currentFrame) && !IsPlaceholderImage(previous))
            {
                DisposeFrame(previous);
            }
        }

        return true;
    }

    private void ApplyPlaceholderFrame()
    {
        using var guard = new StackGuard(nameof(ApplyPlaceholderFrame));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(ApplyPlaceholderFrame));
            }
            catch
            {
                // Ignore invoke failures during shutdown.
            }

            return;
        }

        var previous = Interlocked.Exchange(ref _currentFrame, null);
        if (previous is not null)
        {
            StopAnimationSafe(previous);
        }

        var targetSize = GetPlaceholderTargetSize();
        var placeholderBounds = new Drawing.Rectangle(Drawing.Point.Empty, targetSize);
        if (placeholderBounds == _lastSelectionBounds && IsPlaceholderImage(_target.Image))
        {
            return;
        }

        EnsurePlaceholderBitmap(targetSize);
        var placeholder = _previewPlaceholderBitmap;
        if (placeholder is null)
        {
            if (previous is not null && !IsPlaceholderImage(previous))
            {
                DisposeFrame(previous);
            }

            return;
        }

        _target.Image = placeholder;
        _lastSelectionBounds = placeholderBounds;

        if (previous is not null && !ReferenceEquals(previous, placeholder) && !IsPlaceholderImage(previous))
        {
            DisposeFrame(previous);
        }
    }

    private void UpdateTarget(Drawing.Bitmap frame)
    {
        using var guard = new StackGuard(nameof(UpdateTarget));
        if (!guard.Entered)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

        if (IsEditorPreviewDisabled)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            ApplyPlaceholderFrame();
            return;
        }

        if (_suppressEvents)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

        if (_target.IsDisposed)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

        if (_target.InvokeRequired)
        {
            if (!ShouldDisplayFrame())
            {
                UnregisterPendingFrame(frame);
                frame.Dispose();
                return;
            }

            try
            {
                _target.BeginInvoke(new Action<Drawing.Bitmap>(UpdateTarget), frame);
            }
            catch
            {
                UnregisterPendingFrame(frame);
                frame.Dispose();
            }

            return;
        }

        UnregisterPendingFrame(frame);

        if (!ShouldDisplayFrame())
        {
            frame.Dispose();
            return;
        }

        TryApplyFrame(frame);
    }

    private Drawing.Image? GetLatestFrameForSnapshot()
    {
        var frame = Interlocked.CompareExchange(ref _currentFrame, null, null);
        if (frame is not null)
        {
            if (!IsPlaceholderImage(frame))
            {
                return frame;
            }
        }

        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count > 0)
            {
                return _pendingFrames[^1];
            }
        }

        try
        {
            if (_target.IsDisposed)
            {
                return null;
            }

            if (_target.InvokeRequired)
            {
                return _target.Invoke(new Func<Drawing.Image?>(() =>
                {
                    var image = _target.Image;
                    return IsPlaceholderImage(image) ? null : image;
                }));
            }

            var targetImage = _target.Image;
            return IsPlaceholderImage(targetImage) ? null : targetImage;
        }
        catch
        {
            return null;
        }
    }
}
