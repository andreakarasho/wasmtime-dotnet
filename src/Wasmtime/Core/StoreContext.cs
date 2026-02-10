using Wasmtime.Interop;

namespace Wasmtime;

/// <summary>
/// Represents a reference to the store context within a host function callback.
/// Used for operations that require a store context, such as resource conversions.
/// </summary>
public readonly unsafe struct StoreContext
{
    internal readonly wasmtime_context* Handle;

    internal StoreContext(wasmtime_context* handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Creates a <see cref="StoreContext"/> from a <see cref="Store"/>.
    /// </summary>
    public static StoreContext FromStore(Store store)
    {
        return new StoreContext(store.Context);
    }
}
