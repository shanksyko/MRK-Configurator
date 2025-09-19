using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif

namespace Mieruka.Preview;

#if WINDOWS
[SupportedOSPlatform("windows6.1")]
#endif
public sealed class DwmThumbnailProvider : IDisposable
{
    private bool _disposed;

#if WINDOWS
    private PictureBox? _target;
    private nint _sourceWindowHandle;
    private nint _thumbnailHandle;
#endif

    /// <summary>
    /// Releases unmanaged resources associated with the provider.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

#if WINDOWS
        DetachInternal();
        _sourceWindowHandle = nint.Zero;
#endif
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Detaches the current thumbnail preview from the target control.
    /// </summary>
    public void Detach()
    {
#if WINDOWS
        ThrowIfDisposed();
        DetachInternal();
        _sourceWindowHandle = nint.Zero;
#else
        throw new PlatformNotSupportedException("DWM thumbnails are only supported on Windows.");
#endif
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DwmThumbnailProvider));
        }
    }

#if WINDOWS
    /// <summary>
    /// Gets a value indicating whether the provider has an active DWM thumbnail.
    /// </summary>
    public bool IsAttached => _thumbnailHandle != nint.Zero;

    /// <summary>
    /// Attaches the DWM thumbnail of the specified source window to the provided <see cref="PictureBox"/>.
    /// </summary>
    /// <param name="pictureBox">The WinForms picture box that will host the thumbnail.</param>
    /// <param name="sourceWindowHandle">Handle of the window to preview.</param>
    public void Attach(PictureBox pictureBox, nint sourceWindowHandle)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pictureBox);

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            throw new PlatformNotSupportedException("DWM thumbnails require Windows 7 or newer.");
        }

        if (sourceWindowHandle == nint.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(sourceWindowHandle));
        }

        if (!IsCompositionEnabled())
        {
            throw new InvalidOperationException("Desktop Window Manager composition must be enabled.");
        }

        if (!ReferenceEquals(_target, pictureBox))
        {
            DetachInternal();
            _target = pictureBox;
            SubscribeToTargetEvents(pictureBox);
        }

        _sourceWindowHandle = sourceWindowHandle;

        if (pictureBox.IsHandleCreated)
        {
            RegisterThumbnail();
        }
    }

    /// <summary>
    /// Updates the source window associated with the current thumbnail.
    /// </summary>
    /// <param name="sourceWindowHandle">Handle of the new window to preview.</param>
    public void UpdateSourceWindow(nint sourceWindowHandle)
    {
        ThrowIfDisposed();

        if (_target is null)
        {
            throw new InvalidOperationException("The provider is not attached to a PictureBox.");
        }

        if (sourceWindowHandle == nint.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(sourceWindowHandle));
        }

        _sourceWindowHandle = sourceWindowHandle;

        if (_target.IsHandleCreated)
        {
            RegisterThumbnail();
        }
    }

    private void SubscribeToTargetEvents(PictureBox pictureBox)
    {
        pictureBox.HandleCreated += OnTargetHandleCreated;
        pictureBox.HandleDestroyed += OnTargetHandleDestroyed;
        pictureBox.SizeChanged += OnTargetSizeChanged;
        pictureBox.VisibleChanged += OnTargetVisibilityChanged;
        pictureBox.Disposed += OnTargetDisposed;
    }

    private void UnsubscribeFromTargetEvents(PictureBox pictureBox)
    {
        pictureBox.HandleCreated -= OnTargetHandleCreated;
        pictureBox.HandleDestroyed -= OnTargetHandleDestroyed;
        pictureBox.SizeChanged -= OnTargetSizeChanged;
        pictureBox.VisibleChanged -= OnTargetVisibilityChanged;
        pictureBox.Disposed -= OnTargetDisposed;
    }

    private void OnTargetHandleCreated(object? sender, EventArgs e)
    {
        if (_target is null || sender is not PictureBox pictureBox || !ReferenceEquals(pictureBox, _target))
        {
            return;
        }

        RegisterThumbnail();
    }

    private void OnTargetHandleDestroyed(object? sender, EventArgs e)
    {
        if (sender is not PictureBox pictureBox || !ReferenceEquals(pictureBox, _target))
        {
            return;
        }

        UnregisterThumbnail();
    }

    private void OnTargetSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not PictureBox pictureBox || !ReferenceEquals(pictureBox, _target))
        {
            return;
        }

        UpdateThumbnailProperties();
    }

    private void OnTargetVisibilityChanged(object? sender, EventArgs e)
    {
        if (sender is not PictureBox pictureBox || !ReferenceEquals(pictureBox, _target))
        {
            return;
        }

        UpdateThumbnailProperties();
    }

    private void OnTargetDisposed(object? sender, EventArgs e)
    {
        if (sender is not PictureBox pictureBox || !ReferenceEquals(pictureBox, _target))
        {
            return;
        }

        DetachInternal();
        _sourceWindowHandle = nint.Zero;
    }

    private void RegisterThumbnail()
    {
        if (_target is null || _sourceWindowHandle == nint.Zero)
        {
            return;
        }

        if (!IsCompositionEnabled())
        {
            return;
        }

        UnregisterThumbnail();

        var hr = NativeMethods.DwmRegisterThumbnail(_target.Handle, _sourceWindowHandle, out var thumbnailHandle);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        _thumbnailHandle = thumbnailHandle;
        UpdateThumbnailProperties();
    }

    private void DetachInternal()
    {
        if (_target is null)
        {
            return;
        }

        UnregisterThumbnail();
        UnsubscribeFromTargetEvents(_target);
        _target = null;
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnailHandle == nint.Zero)
        {
            return;
        }

        var hr = NativeMethods.DwmUnregisterThumbnail(_thumbnailHandle);
        _thumbnailHandle = nint.Zero;

        if (hr < 0)
        {
            // Avoid throwing while tearing down the UI. The thumbnail handle is no longer valid anyway.
        }
    }

    private void UpdateThumbnailProperties()
    {
        if (_thumbnailHandle == nint.Zero || _target is null)
        {
            return;
        }

        var clientRectangle = _target.ClientRectangle;
        var isVisible = _target.Visible && clientRectangle.Width > 0 && clientRectangle.Height > 0;

        var properties = new NativeMethods.DwmThumbnailProperties
        {
            dwFlags = NativeMethods.DwmThumbnailPropertyFlags.Visible |
                      NativeMethods.DwmThumbnailPropertyFlags.Opacity |
                      NativeMethods.DwmThumbnailPropertyFlags.SourceClientAreaOnly,
            opacity = 255,
            fVisible = isVisible,
            fSourceClientAreaOnly = false,
        };

        if (isVisible)
        {
            properties.dwFlags |= NativeMethods.DwmThumbnailPropertyFlags.RectDestination;
            properties.rcDestination = CalculateDestinationRect(clientRectangle);
        }

        var hr = NativeMethods.DwmUpdateThumbnailProperties(_thumbnailHandle, ref properties);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private NativeMethods.RECT CalculateDestinationRect(Rectangle clientRectangle)
    {
        if (clientRectangle.Width <= 0 || clientRectangle.Height <= 0)
        {
            return new NativeMethods.RECT(0, 0, 0, 0);
        }

        var targetWidth = clientRectangle.Width;
        var targetHeight = clientRectangle.Height;

        var hr = NativeMethods.DwmQueryThumbnailSourceSize(_thumbnailHandle, out var sourceSize);
        if (hr < 0 || sourceSize.cx <= 0 || sourceSize.cy <= 0)
        {
            return new NativeMethods.RECT(0, 0, targetWidth, targetHeight);
        }

        var scale = Math.Min((double)targetWidth / sourceSize.cx, (double)targetHeight / sourceSize.cy);
        var scaledWidth = Math.Max(1, (int)Math.Round(sourceSize.cx * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(sourceSize.cy * scale));

        var offsetX = (targetWidth - scaledWidth) / 2;
        var offsetY = (targetHeight - scaledHeight) / 2;

        return new NativeMethods.RECT(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);
    }

    private static bool IsCompositionEnabled()
    {
        var hr = NativeMethods.DwmIsCompositionEnabled(out var enabled);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return enabled;
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(nint destinationWindowHandle, nint sourceWindowHandle, out nint thumbnailHandle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(nint thumbnailHandle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(nint thumbnailHandle, ref DwmThumbnailProperties properties);

        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(nint thumbnailHandle, out SIZE size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled(out bool enabled);

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DwmThumbnailProperties
        {
            public DwmThumbnailPropertyFlags dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fSourceClientAreaOnly;
        }

        [Flags]
        public enum DwmThumbnailPropertyFlags : uint
        {
            RectDestination = 0x00000001,
            RectSource = 0x00000002,
            Opacity = 0x00000004,
            Visible = 0x00000008,
            SourceClientAreaOnly = 0x00000010,
        }
    }
#endif
}
