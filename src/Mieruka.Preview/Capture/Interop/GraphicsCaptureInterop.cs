#if WINDOWS10_0_17763_0_OR_GREATER
using System;
using System.Runtime.InteropServices;
using Mieruka.Preview;
using Windows.Graphics.Capture;

namespace Mieruka.Preview.Capture.Interop;

internal static class GraphicsCaptureInterop
{
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);
    private const string GraphicsCaptureItemClassId = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid Direct3DDxgiInterfaceAccessGuid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(string activatableClassId, ref Guid iid, out IntPtr factory);

    public static bool IsWgcRuntimeSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return false;
        }

        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    public static Windows.Graphics.Capture.GraphicsCaptureItem CreateItemForMonitor(nint monitorHandle)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new GraphicsCaptureUnavailableException("Windows Graphics Capture não é suportado neste sistema.", isPermanent: true);
        }

        if (monitorHandle == 0)
        {
            throw new ArgumentException("HMONITOR inválido (IntPtr.Zero).", nameof(monitorHandle));
        }

        var interopGuid = GraphicsCaptureItemInteropGuid;
        var hrFactory = RoGetActivationFactory(GraphicsCaptureItemClassId, ref interopGuid, out var factoryPtr);
        if (hrFactory == E_INVALIDARG)
        {
            ReleaseAndClear(ref factoryPtr);
            throw new GraphicsCaptureUnavailableException(
                "Windows Graphics Capture está indisponível neste host (E_INVALIDARG ao obter GraphicsCaptureItem factory).",
                isPermanent: true,
                new COMException("RoGetActivationFactory retornou E_INVALIDARG.", hrFactory));
        }

        if (hrFactory == REGDB_E_CLASSNOTREG)
        {
            ReleaseAndClear(ref factoryPtr);
            throw new GraphicsCaptureUnavailableException(
                "Windows Graphics Capture não está registrado neste sistema.",
                isPermanent: true,
                new COMException("RoGetActivationFactory retornou REGDB_E_CLASSNOTREG.", hrFactory));
        }

        Marshal.ThrowExceptionForHR(hrFactory);
        try
        {
            var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            var itemGuid = typeof(Windows.Graphics.Capture.GraphicsCaptureItem).GUID;
            IntPtr itemPtr = IntPtr.Zero;

            try
            {
                var hr = factory.CreateForMonitor(monitorHandle, ref itemGuid, out itemPtr);
                if (hr == E_INVALIDARG)
                {
                    throw new COMException("CreateForMonitor retornou E_INVALIDARG.", hr);
                }

                Marshal.ThrowExceptionForHR(hr);

                if (itemPtr == IntPtr.Zero)
                {
                    throw new COMException("CreateForMonitor retornou ponteiro nulo.", E_INVALIDARG);
                }

                var item = (Windows.Graphics.Capture.GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
                itemPtr = IntPtr.Zero;
                return item;
            }
            catch (COMException ex) when (ex.HResult == E_INVALIDARG)
            {
                throw new ArgumentException("Windows Graphics Capture: E_INVALIDARG ao criar item para monitor. Provável handle inválido ou WGC desabilitado por política.", nameof(monitorHandle), ex);
            }
            finally
            {
                ReleaseAndClear(ref itemPtr);
                Marshal.ReleaseComObject(factory);
            }
        }
        finally
        {
            ReleaseAndClear(ref factoryPtr);
        }
    }

    private static void ReleaseAndClear(ref IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }

    public static IntPtr GetInterfaceFromSurface(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface, Guid interfaceId)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var unknown = Marshal.GetIUnknownForObject(surface);
        try
        {
            var accessGuid = Direct3DDxgiInterfaceAccessGuid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref accessGuid, out var accessPtr));
            IDirect3DDxgiInterfaceAccess? access = null;
            try
            {
                access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
                Marshal.ThrowExceptionForHR(access.GetInterface(ref interfaceId, out var nativeResource));
                return nativeResource;
            }
            finally
            {
                if (access is not null)
                {
                    Marshal.ReleaseComObject(access);
                }

                Marshal.Release(accessPtr);
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
#else
namespace Mieruka.Preview.Capture.Interop;

internal static class GraphicsCaptureInterop
{
    public static bool IsWgcRuntimeSupported() => false;
}
#endif
