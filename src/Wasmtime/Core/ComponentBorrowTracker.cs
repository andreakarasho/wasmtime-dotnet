using System;
using System.Collections.Generic;
using Wasmtime.Interop;

namespace Wasmtime;

/// <summary>
/// Tracks borrowed resource handles observed in host imports and drops them
/// when the enclosing component call completes.
/// </summary>
internal static unsafe class ComponentBorrowTracker
{
    [ThreadStatic] private static HashSet<nint>? _borrowed;
    [ThreadStatic] private static int _depth;

    public static void BeginCall()
    {
        if (_depth++ == 0)
        {
            _borrowed ??= new HashSet<nint>();
            _borrowed.Clear();
        }
    }

    public static void AbortCall()
    {
        _depth = 0;
        _borrowed?.Clear();
    }

    public static void TrackArgs(wasmtime_component_val* args, int count)
    {
        if (_depth == 0 || args == null || count == 0)
        {
            return;
        }

        var set = _borrowed ??= new HashSet<nint>();
        for (var i = 0; i < count; i++)
        {
            CollectBorrowedResources(ref args[i], set);
        }
    }

    public static void EndCall(wasmtime_context* context)
    {
        if (_depth == 0)
        {
            return;
        }

        if (--_depth != 0)
        {
            return;
        }

        var set = _borrowed;
        if (set == null || set.Count == 0 || context == null)
        {
            return;
        }

        foreach (var ptr in set)
        {
            var error = WasmtimeSource.wasmtime_component_resource_any_drop(
                context,
                (wasmtime_component_resource_any_t*)ptr);

            if (error != null)
            {
                WasmtimeSource.wasmtime_error_delete(error);
            }
        }

        set.Clear();
    }

    private static void CollectBorrowedResources(ref wasmtime_component_val val, HashSet<nint> set)
    {
        switch (val.kind)
        {
            case 13: // list
            {
                var list = val.of.list;
                var size = (int)list.size;
                for (var i = 0; i < size; i++)
                {
                    CollectBorrowedResources(ref list.data[i], set);
                }
                break;
            }
            case 14: // record
            {
                var record = val.of.record;
                var size = (int)record.size;
                for (var i = 0; i < size; i++)
                {
                    CollectBorrowedResources(ref record.data[i].val, set);
                }
                break;
            }
            case 15: // tuple
            {
                var tuple = val.of.tuple;
                var size = (int)tuple.size;
                for (var i = 0; i < size; i++)
                {
                    CollectBorrowedResources(ref tuple.data[i], set);
                }
                break;
            }
            case 16: // variant
                if (val.of.variant.val != null)
                {
                    CollectBorrowedResources(ref *val.of.variant.val, set);
                }
                break;
            case 18: // option
                if (val.of.option != null)
                {
                    CollectBorrowedResources(ref *val.of.option, set);
                }
                break;
            case 19: // result
                if (val.of.result.val != null)
                {
                    CollectBorrowedResources(ref *val.of.result.val, set);
                }
                break;
            case 21: // resource
                if (val.of.resource != null &&
                    !WasmtimeSource.wasmtime_component_resource_any_owned(val.of.resource))
                {
                    set.Add((nint)val.of.resource);
                }
                break;
        }
    }
}
