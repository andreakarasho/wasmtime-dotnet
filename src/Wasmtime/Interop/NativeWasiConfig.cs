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
    }
}
