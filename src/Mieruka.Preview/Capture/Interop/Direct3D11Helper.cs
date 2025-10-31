#if WINDOWS10_0_17763_0_OR_GREATER
using System;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;

namespace Mieruka.Preview.Capture.Interop;

internal static class Direct3D11Helper
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateDeviceFromDxgiDevice(IDXGIDevice dxgiDevice)
    {
        ArgumentNullException.ThrowIfNull(dxgiDevice);

        var dxgiPtr = dxgiDevice.NativePointer;
        if (dxgiPtr == IntPtr.Zero)
        {
            throw new ArgumentException("IDXGIDevice.NativePointer returned null.", nameof(dxgiDevice));
        }

        var hr = CreateDirect3D11DeviceFromDxgiDevice(dxgiPtr, out var devicePtr);
        try
        {
            Marshal.ThrowExceptionForHR(hr);

            if (devicePtr == IntPtr.Zero)
            {
                throw new COMException("CreateDirect3D11DeviceFromDXGIDevice returned null.", hr);
            }

            return (IDirect3DDevice)Marshal.GetObjectForIUnknown(devicePtr);
        }
        finally
        {
            if (devicePtr != IntPtr.Zero)
            {
                Marshal.Release(devicePtr);
            }
        }
    }
}
#endif
