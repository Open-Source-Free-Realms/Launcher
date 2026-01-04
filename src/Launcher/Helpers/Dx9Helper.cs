using System.Runtime.InteropServices;

public static partial class Dx9Helper
{
    [LibraryImport("d3d9", EntryPoint = "Direct3DCreate9")]
    private static partial nint Direct3DCreate9(uint sdkVersion);

    private const uint D3D_SDK_VERSION = 0x20;

    public static bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var ptr = Direct3DCreate9(D3D_SDK_VERSION);

            if (ptr != nint.Zero)
            {
                Marshal.Release(ptr);

                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}