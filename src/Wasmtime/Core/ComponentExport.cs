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
    public static readonly delegate* unmanaged[Cdecl] <void*, wasmtime_context*, void*, wasmtime_component_val*, nuint, wasmtime_component_val*, nuint, wasmtime_error*> CallerPtr = &Caller;

    /// <summary>
    /// O(1) function lookup by id (the id is the native callback's data pointer). Registration
    /// (linker setup) appends under a lock and grows the array by replacing it; reads happen
    /// on every host import call. Registrations are never removed, so a process that builds
    /// many linkers (e.g. a test suite) keeps growing it — hence no fixed cap.
    /// </summary>
    private static ComponentFunction[] RegisteredFunctions = new ComponentFunction[256];
    private static int FunctionId;
    private static readonly object RegisterGate = new();

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

        var functions = Volatile.Read(ref RegisteredFunctions);
        var id = (nint)data;
        if (id > 0 && id < functions.Length)
        {
            ref var function = ref functions[id];
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
        lock (RegisterGate)
        {
            var id = ++FunctionId;
            var functions = RegisteredFunctions;
            if (id >= functions.Length)
            {
                var grown = new ComponentFunction[functions.Length * 2];
                Array.Copy(functions, grown, functions.Length);
                functions = grown;
            }
            functions[id] = new ComponentFunction(state, function);
            Volatile.Write(ref RegisteredFunctions, functions);
            return id;
        }
    }
}
