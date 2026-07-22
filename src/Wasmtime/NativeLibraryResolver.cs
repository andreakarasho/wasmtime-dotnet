#if NET6_0_OR_GREATER
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wasmtime;

// This fork's native library is deployed as `wasmtime-cm.dll` (component-model)
// instead of the stock `wasmtime.dll`, so it can sit next to the upstream
// `Wasmtime` NuGet's `wasmtime.dll` in the same process without a filename clash.
// The DllImport("wasmtime") sites in Interop/ are redirected to that renamed file
// via a per-assembly resolver installed at module load. If the renamed file is
// absent (e.g. the fork consumed with the stock native name), we fall back to the
// default probing so nothing that already works breaks.
internal static class NativeLibraryResolver
{
    [ModuleInitializer]
    internal static void Install()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "wasmtime")
            return IntPtr.Zero;

        var fileName =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "wasmtime-cm.dll" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libwasmtime-cm.dylib" :
            "libwasmtime-cm.so";

        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(assembly.Location) })
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            var full = Path.Combine(dir, fileName);
            if (File.Exists(full) && NativeLibrary.TryLoad(full, out var handle))
                return handle;
        }

        // Renamed native not found — let the default resolver try the stock name.
        return IntPtr.Zero;
    }
}
#endif
