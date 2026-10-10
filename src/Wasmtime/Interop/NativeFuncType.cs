using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Interop
{
    internal partial struct wasmtime_component_func_type_t
    {
    }

    /// <summary><c>wasmtime_component_valtype_t</c>: the kind, then a pointer to the compound type (unused here).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct wasmtime_component_valtype_t
    {
        internal byte kind;
        internal IntPtr of;
    }

    internal static unsafe partial class WasmtimeSource
    {
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern wasmtime_component_func_type_t* wasmtime_component_func_type(
            wasmtime_component_func* func,
            wasmtime_context* context);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasmtime_component_func_type_delete(wasmtime_component_func_type_t* ty);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern UIntPtr wasmtime_component_func_type_param_count(wasmtime_component_func_type_t* ty);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool wasmtime_component_func_type_param_nth(
            wasmtime_component_func_type_t* ty,
            UIntPtr nth,
            byte** name_ret,
            UIntPtr* name_len_ret,
            wasmtime_component_valtype_t* type_ret);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasmtime_component_valtype_delete(wasmtime_component_valtype_t* ptr);
    }
}
