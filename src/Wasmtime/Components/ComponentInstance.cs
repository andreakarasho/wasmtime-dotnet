using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Wasmtime.Interop;

namespace Wasmtime;

/// <summary>
/// Represents an instance of a WebAssembly component.
/// </summary>
public unsafe class ComponentInstance
{
    private readonly Dictionary<string, ComponentInstanceFunction> _cachedFunctions = new(StringComparer.Ordinal);
    private readonly Component _component;
    private readonly wasmtime_component_instance _handle;
    private readonly Store _store;

    /// <summary>
    /// Reentrancy guard. The component model requires that post_return is called
    /// before the next function call on the same instance. Since stores are
    /// single-threaded and ComponentCallResults is a ref struct (preventing
    /// cross-thread/async usage), a simple boolean flag is sufficient.
    /// </summary>
    internal bool InCall;

    internal ComponentInstance(Component component, wasmtime_component_instance handle, Store store)
    {
        _component = component;
        _handle = handle;
        _store = store;
    }

    /// <summary>
    /// Calls a function in the component instance.
    /// </summary>
    /// <param name="name">Name of the function to call.</param>
    /// <param name="resultCount">Number of results to expect from the call.</param>
    /// <param name="values">Arguments to pass to the function.</param>
    /// <returns>The results of the function call.</returns>
    public ComponentCallResults Call(string name, int resultCount, ReadOnlySpan<ComponentValue> values)
    {
        fixed (ComponentValue* valuesPtr = values)
        {
            return Call(name, resultCount, valuesPtr, values.Length);
        }
    }

    /// <summary>
    /// Calls a function in the component instance.
    /// </summary>
    /// <param name="name">Name of the function to call.</param>
    /// <param name="resultCount">Number of results to expect from the call.</param>
    /// <param name="values">Arguments to pass to the function.</param>
    /// <param name="valuesLength">Length of the arguments array.</param>
    /// <returns>The results of the function call.</returns>
    public ComponentCallResults Call(string name, int resultCount, ComponentValue* values, int valuesLength)
    {
        return Call(GetFunction(name), resultCount, values, valuesLength);
    }

    /// <summary>
    /// Calls a function in the component instance.
    /// </summary>
    /// <param name="function">Instance of <see cref="GetFunction"/> to call.</param>
    /// <param name="resultCount">Number of results to expect from the call.</param>
    /// <param name="values">Arguments to pass to the function.</param>
    /// <returns>The results of the function call.</returns>
    public ComponentCallResults Call(ComponentInstanceFunction function, int resultCount, ReadOnlySpan<ComponentValue> values)
    {
        fixed (ComponentValue* valuesPtr = values)
        {
            return Call(function, resultCount, valuesPtr, values.Length);
        }
    }

    /// <summary>
    /// Calls a function in the component instance.
    /// </summary>
    /// <param name="function">Instance of <see cref="GetFunction"/> to call.</param>
    /// <param name="resultCount">Number of results to expect from the call.</param>
    /// <param name="values">Arguments to pass to the function.</param>
    /// <param name="valuesLength">Length of the arguments array.</param>
    /// <returns>The results of the function call.</returns>
    public ComponentCallResults Call(ComponentInstanceFunction function, int resultCount, ComponentValue* values, int valuesLength)
    {
        if (InCall)
        {
            ThrowReentrancy();
        }

        InCall = true;

        try
        {
#if NET
            ObjectDisposedException.ThrowIf(_store.Disposed, nameof(Store));
#else
            if (_store.Disposed) throw new ObjectDisposedException(nameof(Store));
#endif

            var results = ComponentCallResultsInternal.ThreadInstance;
            results.Initialize(resultCount, function.Function, _store.Context, this);

            ComponentBorrowTracker.BeginCall();

            fixed (ComponentValue* resultsPtr = results.Array)
            {
                var error = wasmtime_component_func_call(
                    &function.Function,
                    _store.Context,
                    (wasmtime_component_val*)values,
                    (nuint)valuesLength,
                    (wasmtime_component_val*)resultsPtr,
                    (nuint)results.Length
                );

                WasmtimeException.ThrowIfError(error);
            }

            return new ComponentCallResults(results);
        }
        catch
        {
            // Reset the thread-static instance so it's not left dirty for the next call.
            // Don't call Dispose (which invokes post_return) since the call itself failed.
            ComponentCallResultsInternal.ThreadInstance.Reset();
            ComponentBorrowTracker.AbortCall();
            InCall = false;
            throw;
        }
    }

    /// <summary>
    /// Gets the function in the component instance with the specified name.
    /// </summary>
    /// <param name="name">Name of the function to get.</param>
    /// <returns>The function with the specified name.</returns>
    /// <exception cref="WasmtimeException">Thrown if the function is not found in the component instance.</exception>
    /// <remarks>
    /// This method is not thread-safe.
    /// </remarks>
    public ComponentInstanceFunction GetFunction(string name)
    {
        return _cachedFunctions.TryGetValue(name, out var handle)
            ? handle
            : LoadFunction(name);
    }

    /// <summary>
    /// Gets a function exported within an interface instance (e.g. an exported resource's
    /// constructor/methods, which are namespaced under the interface rather than at the root).
    /// </summary>
    public ComponentInstanceFunction GetFunction(string interfacePath, string name)
    {
        var key = interfacePath + " " + name;
        return _cachedFunctions.TryGetValue(key, out var handle)
            ? handle
            : LoadFunctionNested(interfacePath, name, key);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ComponentInstanceFunction LoadFunctionNested(string interfacePath, string name, string key)
    {
#if NET
        ObjectDisposedException.ThrowIf(_store.Disposed, nameof(Store));
#else
        if (_store.Disposed) throw new ObjectDisposedException(nameof(Store));
#endif

        if (!_component.TryGetExport(interfacePath, name, out var index))
        {
            throw new WasmtimeException($"Function '{name}' not found in interface '{interfacePath}'");
        }

        byte success;
        wasmtime_component_func func;

        fixed (wasmtime_component_instance* instance = &_handle)
        {
            success = wasmtime_component_instance_get_func(instance, _store.Context, index, &func);
        }

        if (success != 1)
        {
            throw new WasmtimeException($"Export '{name}' is not a function");
        }

        var function = new ComponentInstanceFunction(func);
        _cachedFunctions[key] = function;
        return function;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ComponentInstanceFunction LoadFunction(string name)
    {
#if NET
        ObjectDisposedException.ThrowIf(_store.Disposed, nameof(Store));
#else
        if (_store.Disposed) throw new ObjectDisposedException(nameof(Store));
#endif

        if (!_component.TryGetExport(name, out var index))
        {
            throw new WasmtimeException($"Function '{name}' not found in component");
        }

        byte success;
        wasmtime_component_func func;

        fixed (wasmtime_component_instance* instance = &_handle)
        {
            success = wasmtime_component_instance_get_func(instance, _store.Context, index, &func);
        }

        if (success != 1)
        {
            throw new WasmtimeException($"Export '{name}' is not a function");
        }

        var function = new ComponentInstanceFunction(func);
        _cachedFunctions[name] = function;
        return function;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowReentrancy()
    {
        throw new InvalidOperationException(
            "Cannot call a component function while another call is in progress. " +
            "Dispose the previous ComponentCallResults first.");
    }
}
