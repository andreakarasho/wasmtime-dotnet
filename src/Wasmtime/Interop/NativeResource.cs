using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Interop
{
    internal partial struct wasmtime_component_resource_type_t
    {
    }

    internal partial struct wasmtime_component_resource_any_t
    {
    }

    internal partial struct wasmtime_component_resource_host_t
    {
    }

    internal unsafe partial struct wasmtime_component_valunion_t
    {
        [FieldOffset(0)]
        internal wasmtime_component_resource_any_t* resource;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: NativeTypeName("wasmtime_error_t *")]
    internal unsafe delegate wasmtime_error* wasmtime_component_resource_destructor_t(
        void* data,
        [NativeTypeName("wasmtime_context_t *")] wasmtime_context* context,
        uint rep);

    internal static unsafe partial class WasmtimeSource
    {
        // Resource type registration
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern wasmtime_component_resource_type_t* wasmtime_component_resource_type_new_host(uint ty);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasmtime_component_resource_type_delete(wasmtime_component_resource_type_t* resource);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("wasmtime_error_t *")]
        internal static extern wasmtime_error* wasmtime_component_linker_instance_add_resource(
            wasmtime_component_linker_instance_t* linker_instance,
            [NativeTypeName("const char *")] byte* name,
            [NativeTypeName("size_t")] UIntPtr name_len,
            [NativeTypeName("const wasmtime_component_resource_type_t *")] wasmtime_component_resource_type_t* resource,
            [NativeTypeName("wasmtime_component_resource_destructor_t")] IntPtr destructor,
            void* data,
            [NativeTypeName("void (*)(void *)")] IntPtr finalizer);

        // Host resource creation
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern wasmtime_component_resource_host_t* wasmtime_component_resource_host_new(
            [MarshalAs(UnmanagedType.U1)] bool owned,
            uint rep,
            uint ty);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasmtime_component_resource_host_delete(
            wasmtime_component_resource_host_t* resource);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern uint wasmtime_component_resource_host_rep(
            [NativeTypeName("const wasmtime_component_resource_host_t *")] wasmtime_component_resource_host_t* resource);

        // Host <-> Any conversions (require store context)
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("wasmtime_error_t *")]
        internal static extern wasmtime_error* wasmtime_component_resource_host_to_any(
            [NativeTypeName("wasmtime_context_t *")] wasmtime_context* ctx,
            [NativeTypeName("const wasmtime_component_resource_host_t *")] wasmtime_component_resource_host_t* resource,
            wasmtime_component_resource_any_t** ret);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("wasmtime_error_t *")]
        internal static extern wasmtime_error* wasmtime_component_resource_any_to_host(
            [NativeTypeName("wasmtime_context_t *")] wasmtime_context* ctx,
            [NativeTypeName("const wasmtime_component_resource_any_t *")] wasmtime_component_resource_any_t* resource,
            wasmtime_component_resource_host_t** ret);

        // Generic resource operations
        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool wasmtime_component_resource_any_owned(
            [NativeTypeName("const wasmtime_component_resource_any_t *")] wasmtime_component_resource_any_t* resource);

        [DllImport("wasmtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void wasmtime_component_resource_any_delete(
            wasmtime_component_resource_any_t* resource);
    }
}
