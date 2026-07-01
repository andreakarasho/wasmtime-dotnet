using System;
using Wasmtime.Interop;

namespace Wasmtime;

internal unsafe class ComponentCallResultsInternal : IDisposable
{
    [ThreadStatic] private static ComponentCallResultsInternal? _cachedInstance;

    internal static ComponentCallResultsInternal ThreadInstance => _cachedInstance ??= new ComponentCallResultsInternal();

    // Reused per-thread; grows only when a call returns more results than the current capacity.
    public ComponentValue[] Array;
    private wasmtime_component_func _func;
    private wasmtime_context* _context;
    private ComponentInstance? _instance;

    private const int DefaultCapacity = 16;

    private ComponentCallResultsInternal()
    {
        Array = new ComponentValue[DefaultCapacity];
    }

    public int Length { get; private set; }

    internal void Initialize(int count, wasmtime_component_func func, wasmtime_context* context, ComponentInstance instance)
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException("This instance is already in use.");
        }

        // Guard against buffer overflow when wasmtime writes `count` results into Array;
        // grow on demand (reused afterwards) rather than capping.
        if (count > Array.Length)
        {
            Array = new ComponentValue[count];
        }

        Length = count;
        _func = func;
        _context = context;
        _instance = instance;
    }

    /// <summary>
    /// Resets this instance without calling post_return or clearing the reentrancy flag.
    /// Used when the function call itself failed (error from wasmtime_component_func_call)
    /// so post_return should not be called, but the thread-static state must be cleaned up.
    /// The caller is responsible for clearing the reentrancy flag separately.
    /// </summary>
    internal void Reset()
    {
        System.Array.Clear(Array, 0, Length);
        _instance = null;
        _context = null;
        _func = default;
        Length = 0;
    }

    public void Dispose()
    {
        if (_instance is not {} instance)
        {
            throw new ObjectDisposedException(nameof(ComponentCallResultsInternal));
        }

        ComponentBorrowTracker.EndCall(_context);

        fixed (wasmtime_component_func* ptr = &_func)
        {
            wasmtime_component_func_post_return(ptr, _context);
        }

        // Free the host-side copies wasmtime lowered into the embedder-owned results array.
        // post_return (above) only reclaims guest buffers, so without this every composite return
        // (string/list/record/tuple/variant/flags/option/result) leaks. Resource handles are
        // skipped inside FreeReturned (retained by wrappers / released by the borrow tracker).
        for (var i = 0; i < Length; i++)
        {
            Array[i].FreeReturned();
        }

        System.Array.Clear(Array, 0, Length);

        _instance = null;
        _context = null;
        _func = default;
        Length = 0;

        instance.InCall = false;
    }
}
