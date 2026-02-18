using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Wasmtime.Interop;

namespace Wasmtime;

public unsafe delegate void ComponentFunctionDelegate(object? state, ComponentCallResults args, ComponentValue* results, StoreContext context);

internal record struct ComponentFunction(
    object? State,
    ComponentFunctionDelegate Function
);

internal unsafe class ComponentExport
{
    /// <summary>
    /// Maximum number of functions that can be registered.
    /// </summary>
    private const int MaxFunctions = 1024;

    public static readonly delegate* unmanaged[Cdecl] <void*, wasmtime_context*, void*, wasmtime_component_val*, nuint, wasmtime_component_val*, nuint, wasmtime_error*> CallerPtr = &Caller;

    /// <summary>
    /// Pre-allocated array for O(1) function lookup. Registration is write-once (during linker setup),
    /// reads happen on every host import call. Volatile.Read/Write ensures visibility across threads.
    /// </summary>
    private static readonly ComponentFunction[] RegisteredFunctions = new ComponentFunction[MaxFunctions];
    private static int FunctionId;
    private static nint StaticFunctionIds;

    static ComponentExport()
    {
        StaticFunctionIds = Marshal.AllocHGlobal(MaxFunctions);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static wasmtime_error* Caller(
        void* data,
        wasmtime_context* context,
        void* funcType,
        wasmtime_component_val* argsPtr,
        nuint nargs,
        wasmtime_component_val* resultsPtr,
        nuint nresults)
    {
        var args = new ComponentCallResults(argsPtr, (int)nargs);
        ComponentBorrowTracker.TrackArgs(argsPtr, (int)nargs);

        string? errorMessage = null;

        var id = (int)((nint)data - StaticFunctionIds);
        if (id > 0 && id < MaxFunctions)
        {
            ref var function = ref RegisteredFunctions[id];
            if (function.Function != null)
            {
                try
                {
                    function.Function(function.State, args, (ComponentValue*)resultsPtr, new StoreContext(context));
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }
            else
            {
                errorMessage = "Function not found";
            }
        }
        else
        {
            errorMessage = "Function not found";
        }

        if (errorMessage is null)
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetMaxByteCount(errorMessage.Length) <= 255
            ? stackalloc byte[256]
            : new byte[Encoding.UTF8.GetByteCount(errorMessage) + 1];

        fixed (char* utf16 = errorMessage)
        fixed (byte* utf8 = bytes)
        {
            var len = Encoding.UTF8.GetBytes(utf16, errorMessage.Length, utf8, bytes.Length - 1);
            bytes[len] = 0;

            return wasmtime_error_new(utf8);
        }
    }

    public static nint RegisterFunction(ComponentFunctionDelegate function, object? state = null)
    {
        var id = Interlocked.Increment(ref FunctionId);

        if (id >= MaxFunctions)
        {
            throw new InvalidOperationException("Maximum number of functions reached.");
        }

        RegisteredFunctions[id] = new ComponentFunction(state, function);

        return StaticFunctionIds + id;
    }
}
