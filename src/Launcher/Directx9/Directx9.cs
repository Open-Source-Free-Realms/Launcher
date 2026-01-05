using System;
using System.Runtime.InteropServices;

public static partial class D3D9
{
    [LibraryImport("d3d9.dll", EntryPoint = "Direct3DCreate9")] 
    public static partial nint Direct3DCreate9(uint sdkVersion);

    const uint D3D_SDK_VERSION = 0x20;

    public static bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            IntPtr v = Direct3DCreate9(D3D_SDK_VERSION);
            if (v != IntPtr.Zero)
            {
                Marshal.Release(v);
                return true;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (Exception)
        {
        }
        return false;
    }
}