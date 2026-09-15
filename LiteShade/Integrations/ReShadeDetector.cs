using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiteShade.Integrations;

internal static class ReShadeDetector
{
    public static bool IsLoaded()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                using (module)
                {
                    if (!GetModuleHandleEx(4, module.BaseAddress, out var handle))
                    {
                        continue;
                    }

                    try
                    {
                        if (NativeLibrary.TryGetExport(handle, "ReShadeRegisterAddon", out _)
                            && NativeLibrary.TryGetExport(handle, "ReShadeRegisterEvent", out _))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        FreeLibrary(handle);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }

        return false;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleEx(uint flags, nint address, out nint module);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
