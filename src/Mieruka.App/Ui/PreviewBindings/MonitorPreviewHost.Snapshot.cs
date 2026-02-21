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
    private async Task<bool> CaptureEditorSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _target.IsDisposed)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var source = GetLatestFrameForSnapshot();
        if (source is null)
        {
            _logger.Warning("EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason}", "SemFrame");
            return false;
        }

        if (!TryGetFrameSize(source, out var width, out var height) || width <= 0 || height <= 0)
        {
            _logger.Warning("EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason}", "FrameInvalido");
            return false;
        }

        Drawing.Bitmap? snapshot = null;

        try
        {
            snapshot = new Drawing.Bitmap(source);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            _logger.Warning(ex, "EditorSnapshot: não foi possível clonar frame para snapshot");
            return false;
        }
        catch (Exception ex) when (ex is OutOfMemoryException)
        {
            _logger.Warning(ex, "EditorSnapshot: falha de memória ao clonar frame para snapshot");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var applied = await RenderEditorSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(true);
        if (applied)
        {
            _logger.Information(
                "EditorSnapshot: snapshot capturado para monitor {MonitorId}, bounds={Width}x{Height}",
                MonitorId,
                width,
                height);
        }

        return applied;
    }

    private Task<bool> RenderEditorSnapshotAsync(Drawing.Bitmap snapshot, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || _disposed || _target.IsDisposed)
            {
                TryDisposeSnapshot(snapshot);
                completion.TrySetResult(false);
                return;
            }

            var previousSnapshot = Interlocked.Exchange(ref _editorSnapshotBitmap, snapshot);
            _editorSnapshotModeEnabled = true;

            if (!TryApplyFrame(snapshot))
            {
                _editorSnapshotModeEnabled = false;
                Interlocked.Exchange(ref _editorSnapshotBitmap, null);
                if (previousSnapshot is not null && !ReferenceEquals(previousSnapshot, snapshot))
                {
                    TryDisposeSnapshot(previousSnapshot);
                }

                completion.TrySetResult(false);
                return;
            }

            if (previousSnapshot is not null && !ReferenceEquals(previousSnapshot, snapshot))
            {
                TryDisposeSnapshot(previousSnapshot);
            }

            _logger.Debug(
                "EditorSnapshot: overlays aplicados sobre snapshot estático monitor={MonitorId}",
                MonitorId);
            completion.TrySetResult(true);
        }

        try
        {
            if (_target.InvokeRequired)
            {
                _target.BeginInvoke(new Action(Apply));
            }
            else
            {
                Apply();
            }
        }
        catch
        {
            TryDisposeSnapshot(snapshot);
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    private bool DisableEditorSnapshot(bool clearImage)
    {
        var snapshot = Interlocked.Exchange(ref _editorSnapshotBitmap, null);
        var hadSnapshot = snapshot is not null || _editorSnapshotModeEnabled;
        _editorSnapshotModeEnabled = false;

        if (snapshot is null)
        {
            return hadSnapshot;
        }

        void DisposeSnapshot()
        {
            if (clearImage && ReferenceEquals(_currentFrame, snapshot))
            {
                StopAnimationSafe(snapshot);
                Interlocked.Exchange(ref _currentFrame, null);

                if (!_target.IsDisposed && ReferenceEquals(_target.Image, snapshot))
                {
                    _target.Image = null;
                }
            }

            TryDisposeSnapshot(snapshot);
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(DisposeSnapshot));
            }
            catch
            {
                TryDisposeSnapshot(snapshot);
            }
        }
        else
        {
            DisposeSnapshot();
        }

        return hadSnapshot;
    }

    private static void TryDisposeSnapshot(Drawing.Bitmap snapshot)
    {
        try
        {
            snapshot.Dispose();
        }
        catch
        {
        }
    }

    private bool TryGetFrameSize(Drawing.Image frame, out int width, out int height)
    {
        try
        {
            width = frame.Width;
            height = frame.Height;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            ForEvent("FrameDiscarded").Debug(ex, "Quadro de pré-visualização descartado por imagem inválida.");
            width = 0;
            height = 0;
            return false;
        }
    }

    private bool StopAnimationSafe(Drawing.Image image)
    {
        if (!CanAnimate(image))
        {
            return false;
        }

        try
        {
            Drawing.ImageAnimator.StopAnimate(image, _frameAnimationHandler);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartAnimationSafe(Drawing.Image image)
    {
        // Preview frames are always single-frame bitmaps; skip the expensive
        // ImageAnimator.CanAnimate check entirely on the hot path.
        // Animated GIFs are never produced by the capture pipeline.
    }

    private static bool CanAnimate(Drawing.Image image)
    {
        try
        {
            return Drawing.ImageAnimator.CanAnimate(image);
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            return false;
        }
    }

    private void DisposeFrame(Drawing.Image frame)
    {
        // Do not dispose bitmaps that belong to the pool ring buffer -" they are reused.
        for (var i = 0; i < _bitmapPool.Length; i++)
        {
            if (ReferenceEquals(_bitmapPool[i], frame))
            {
                return;
            }
        }

        try
        {
            frame.Dispose();
        }
        catch
        {
            // Ignore dispose failures.
        }
    }

    private void ClearFrame()
    {
        using var guard = new StackGuard(nameof(ClearFrame));
        if (!guard.Entered)
        {
            return;
        }

        if (_target.IsDisposed)
        {
            if (_currentFrame is not null)
            {
                StopAnimationSafe(_currentFrame);
                DisposeFrame(_currentFrame);
            }
            _currentFrame = null;
            ResetFrameThrottle();
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(ClearFrame));
            }
            catch
            {
                // Ignore invoke failures during shutdown.
            }

            return;
        }

        if (ReferenceEquals(_target.Image, _currentFrame) || IsPlaceholderImage(_target.Image))
        {
            _target.Image = null;
        }

        if (_currentFrame is not null)
        {
            StopAnimationSafe(_currentFrame);
            if (!IsPlaceholderImage(_currentFrame))
            {
                DisposeFrame(_currentFrame);
            }
            _currentFrame = null;
        }
        _lastSelectionBounds = Drawing.Rectangle.Empty;
        ResetFrameThrottle();
    }

    private void DisposePlaceholderBitmap()
    {
        var placeholder = Interlocked.Exchange(ref _previewPlaceholderBitmap, null);
        if (placeholder is null)
        {
            return;
        }

        _lastSelectionBounds = Drawing.Rectangle.Empty;

        try
        {
            placeholder.Dispose();
        }
        catch
        {
        }
    }

    private void DisposeBitmapPool()
    {
        for (var i = 0; i < _bitmapPool.Length; i++)
        {
            var bitmap = Interlocked.Exchange(ref _bitmapPool[i], null);
            if (bitmap is not null)
            {
                try
                {
                    bitmap.Dispose();
                }
                catch
                {
                    // Ignore failures while disposing pooled bitmaps.
                }
            }
        }
    }
}
