#if WINDOWS10_0_17763_0_OR_GREATER
using System;
using System.Runtime.InteropServices;

namespace Mieruka.Preview.Capture.Interop;

internal static class GraphicsCaptureInterop
{
    private const string GraphicsCaptureItemClassId = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid Direct3DDxgiInterfaceAccessGuid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(string activatableClassId, ref Guid iid, out IntPtr factory);

    public static Windows.Graphics.Capture.GraphicsCaptureItem CreateItemForMonitor(nint monitorHandle)
    {
        if (monitorHandle == 0)
        {
            throw new ArgumentNullException(nameof(monitorHandle));
        }

        Marshal.ThrowExceptionForHR(RoGetActivationFactory(GraphicsCaptureItemClassId, ref GraphicsCaptureItemInteropGuid, out var factoryPtr));
        try
        {
            var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            IntPtr itemPtr = IntPtr.Zero;

            try
            {
                var itemGuid = typeof(Windows.Graphics.Capture.GraphicsCaptureItem).GUID;
                Marshal.ThrowExceptionForHR(factory.CreateForMonitor(monitorHandle, ref itemGuid, out itemPtr));
                return (Windows.Graphics.Capture.GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
            }
            finally
            {
                if (itemPtr != IntPtr.Zero)
                {
                    Marshal.Release(itemPtr);
                }

                Marshal.ReleaseComObject(factory);
            }
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero)
            {
                Marshal.Release(factoryPtr);
            }
        }
    }

    public static Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice CreateDirect3DDevice(IntPtr devicePointer)
    {
        if (devicePointer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(devicePointer));
        }

        try
        {
            return (Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice)Marshal.GetObjectForIUnknown(devicePointer);
        }
        finally
        {
            Marshal.Release(devicePointer);
        }
    }

    public static IntPtr GetInterfaceFromSurface(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface, Guid interfaceId)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var unknown = Marshal.GetIUnknownForObject(surface);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref Direct3DDxgiInterfaceAccessGuid, out var accessPtr));
            IDirect3DDxgiInterfaceAccess access = null!;
            try
            {
                access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
            }
            finally
            {
                Marshal.Release(accessPtr);
            }

            try
            {
                Marshal.ThrowExceptionForHR(access.GetInterface(ref interfaceId, out var nativeResource));
                return nativeResource;
            }
            finally
            {
                Marshal.ReleaseComObject(access);
            }
        }
        finally
        {
            if (unknown != IntPtr.Zero)
            {
                Marshal.Release(unknown);
            }
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        int GetInterface(ref Guid iid, out IntPtr resource);
    }
}
#endif
