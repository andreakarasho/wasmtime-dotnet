using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Interop
{
    internal static unsafe partial class WasmtimeSource
    {
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern wasi_config_t* wasi_config_new();

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasi_config_inherit_stdin(wasi_config_t* config);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasi_config_inherit_stdout(wasi_config_t* config);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasi_config_inherit_stderr(wasi_config_t* config);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasi_config_delete(wasi_config_t* config);

        // Sets the guest's argv. WASI defaults to an EMPTY argv, which the .NET
        // wasi runtime startup (GetMainMethodArguments) trips over — it expects at
        // least argv[0]. Setting a single program-name element fixes the overflow.
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool wasi_config_set_argv(
            wasi_config_t* config,
            nuint argc,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] argv);

        // Grants the guest WASI filesystem access to a host directory. perms are
        // bitmasks: WASMTIME_WASI_DIR/FILE_PERMS_READ = 1, _WRITE = 2.
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool wasi_config_preopen_dir(
            wasi_config_t* config,
            // LPUTF8Str is absent from netstandard2.0's UnmanagedType; that TFM is
            // packaging-only (the host loads net8/9+ at runtime), so LPStr there just
            // satisfies the compile — ASCII preopen paths marshal identically.
#if NETSTANDARD2_0
            [MarshalAs(UnmanagedType.LPStr)] string hostPath,
            [MarshalAs(UnmanagedType.LPStr)] string guestPath,
#else
            [MarshalAs(UnmanagedType.LPUTF8Str)] string hostPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string guestPath,
#endif
            nuint dirPerms,
            nuint filePerms);
    }
}
