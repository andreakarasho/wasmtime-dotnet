using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Interop
{
    /// <summary>
    /// [SuppressGCTransition] variants of hot-path P/Invoke functions.
    /// These skip the managed→cooperative→preemptive GC transition on each call,
    /// reducing per-call overhead from ~40ns to ~10ns. Only safe for native functions
    /// that complete quickly (&lt;1μs), don't allocate GC memory, and don't call back
    /// into managed code. The allocation functions here (malloc + memcpy) qualify.
    /// </summary>
    internal static unsafe partial class WasmtimeSource
    {
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "wasm_byte_vec_new")]
        [SuppressGCTransition]
        internal static extern void wasm_byte_vec_new_fast(
            wasm_byte_vec_t* @out,
            [NativeTypeName("size_t")] UIntPtr size,
            [NativeTypeName("const wasm_byte_t[]")] byte* data);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "wasmtime_component_valrecord_new_uninit")]
        [SuppressGCTransition]
        internal static extern void wasmtime_component_valrecord_new_uninit_fast(
            [NativeTypeName("wasmtime_component_valrecord_t *")] wasmtime_component_valrecord* @out,
            [NativeTypeName("size_t")] UIntPtr size);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "wasmtime_component_vallist_new_uninit")]
        [SuppressGCTransition]
        internal static extern void wasmtime_component_vallist_new_uninit_fast(
            [NativeTypeName("wasmtime_component_vallist_t *")] wasmtime_component_vallist* @out,
            [NativeTypeName("size_t")] UIntPtr size);
    }
}
