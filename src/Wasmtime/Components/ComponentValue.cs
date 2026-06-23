using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wasmtime.Interop;

namespace Wasmtime;

/// <summary>
/// Represents a value used in component calls for Wasmtime.
/// </summary>
public struct ComponentValue : IDisposable
{
    #if DEBUG
    private static int _activeCount;

    public static int ActiveCount => _activeCount;
    #else
    public static int ActiveCount => 0;
    #endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementActiveCount(bool externallyOwned)
    {
        #if DEBUG
        if (!externallyOwned)
        {
            System.Threading.Interlocked.Increment(ref _activeCount);
        }
        #endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecrementActiveCount()
    {
        #if DEBUG
        // ponytail: best-effort counter only. Creation increments solely for non-externally-owned
        // values, but Dispose always decrements, so the count legitimately goes negative for
        // externally-owned views and across parallel tests. Do NOT throw on negative — it is a
        // false alarm, not a double-free signal (externallyOwned has no other effect).
        System.Threading.Interlocked.Decrement(ref _activeCount);
        #endif
    }

    // ** DO NOT ADD FIELDS TO THIS STRUCTURE. **
    // This struct is a direct mapping to the native wasmtime_component_val structure.
    // Adding fields will change the memory layout and break interop.
    private wasmtime_component_val _val;

    internal wasmtime_component_val Value => _val;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with an existing native handle.
    /// </summary>
    /// <param name="val">Pointer to the native component value.</param>
    internal ComponentValue(wasmtime_component_val val, bool externallyOwned)
    {
        _val = val;
        IncrementActiveCount(externallyOwned);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a boolean value.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    public ComponentValue(bool value)
    {
        _val.kind = 0;
        _val.of.boolean = value ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with an <see cref="sbyte"/> value.
    /// </summary>
    /// <param name="value">The <see cref="sbyte"/> value.</param>
    public ComponentValue(sbyte value)
    {
        _val.kind = 1;
        _val.of.s8 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="byte"/> value.
    /// </summary>
    /// <param name="value">The <see cref="byte"/> value.</param>
    public ComponentValue(byte value)
    {
        _val.kind = 2;
        _val.of.u8 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="short"/> value.
    /// </summary>
    /// <param name="value">The <see cref="short"/> value.</param>
    public ComponentValue(short value)
    {
        _val.kind = 3;
        _val.of.s16 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="ushort"/> value.
    /// </summary>
    /// <param name="value">The <see cref="ushort"/> value.</param>
    public ComponentValue(ushort value)
    {
        _val.kind = 4;
        _val.of.u16 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with an <see cref="int"/> value.
    /// </summary>
    /// <param name="value">The <see cref="int"/> value.</param>
    public ComponentValue(int value)
    {
        _val.kind = 5;
        _val.of.s32 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="uint"/> value.
    /// </summary>
    /// <param name="value">The <see cref="uint"/> value.</param>
    public ComponentValue(uint value)
    {
        _val.kind = 6;
        _val.of.u32 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="long"/> value.
    /// </summary>
    /// <param name="value">The <see cref="long"/> value.</param>
    public ComponentValue(long value)
    {
        _val.kind = 7;
        _val.of.s64 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="ulong"/> value.
    /// </summary>
    /// <param name="value">The <see cref="ulong"/> value.</param>
    public ComponentValue(ulong value)
    {
        _val.kind = 8;
        _val.of.u64 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="float"/> value.
    /// </summary>
    /// <param name="value">The <see cref="float"/> value.</param>
    public ComponentValue(float value)
    {
        _val.kind = 9;
        _val.of.f32 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="double"/> value.
    /// </summary>
    /// <param name="value">The <see cref="double"/> value.</param>
    public ComponentValue(double value)
    {
        _val.kind = 10;
        _val.of.f64 = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="char"/> value.
    /// </summary>
    /// <param name="value">The <see cref="char"/> value.</param>
    public ComponentValue(char value)
    {
        _val.kind = 11;
        _val.of.character = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="string"/> value.
    /// </summary>
    /// <param name="value">The <see cref="string"/> value.</param>
    public ComponentValue(string value, bool externallyOwned)
    {
        _val.kind = 12;
        _val.of.@string = new ByteVector(value).Value;
        IncrementActiveCount(externallyOwned);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="ListBuilder"/> value.
    /// </summary>
    /// <param name="value">The <see cref="RecordBuilder"/> value.</param>
    public ComponentValue(ListBuilder value, bool externallyOwned)
    {
        _val.kind = 13;
        _val.of.list = value.Value;
        IncrementActiveCount(externallyOwned);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentValue"/> struct with a <see cref="RecordBuilder"/> value.
    /// </summary>
    /// <param name="value">The <see cref="RecordBuilder"/> value.</param>
    public ComponentValue(RecordBuilder value, bool externallyOwned)
    {
        _val.kind = 14;
        _val.of.record = value.Value;
        IncrementActiveCount(externallyOwned);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a boolean value.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the boolean.</returns>
    public static ComponentValue CreateBoolean(bool value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from an <see cref="sbyte"/> value.
    /// </summary>
    /// <param name="value">The <see cref="sbyte"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the sbyte.</returns>
    public static ComponentValue CreateSByte(sbyte value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="byte"/> value.
    /// </summary>
    /// <param name="value">The <see cref="byte"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the byte.</returns>
    public static ComponentValue CreateByte(byte value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="short"/> value.
    /// </summary>
    /// <param name="value">The <see cref="short"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the short.</returns>
    public static ComponentValue CreateInt16(short value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="ushort"/> value.
    /// </summary>
    /// <param name="value">The <see cref="ushort"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the ushort.</returns>
    public static ComponentValue CreateUInt16(ushort value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from an <see cref="int"/> value.
    /// </summary>
    /// <param name="value">The <see cref="int"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the int.</returns>
    public static ComponentValue CreateInt32(int value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="uint"/> value.
    /// </summary>
    /// <param name="value">The <see cref="uint"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the uint.</returns>
    public static ComponentValue CreateUInt32(uint value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="long"/> value.
    /// </summary>
    /// <param name="value">The <see cref="long"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the long.</returns>
    public static ComponentValue CreateInt64(long value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="ulong"/> value.
    /// </summary>
    /// <param name="value">The <see cref="ulong"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the ulong.</returns>
    public static ComponentValue CreateUInt64(ulong value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="float"/> value.
    /// </summary>
    /// <param name="value">The <see cref="float"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the float.</returns>
    public static ComponentValue CreateFloat(float value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="double"/> value.
    /// </summary>
    /// <param name="value">The <see cref="double"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the double.</returns>
    public static ComponentValue CreateDouble(double value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="char"/> value.
    /// </summary>
    /// <param name="value">The <see cref="char"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the char.</returns>
    public static ComponentValue CreateChar(char value) => new(value);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="string"/> value.
    /// </summary>
    /// <param name="value">The <see cref="string"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the string.</returns>
    public static ComponentValue CreateString(string value, bool externallyOwned) => new(value, externallyOwned);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="RecordBuilder"/> value.
    /// </summary>
    /// <param name="value">The <see cref="RecordBuilder"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the memory.</returns>
    public static ComponentValue CreateRecord(RecordBuilder value, bool externallyOwned) => new(value, externallyOwned);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a <see cref="ListBuilder"/> value.
    /// </summary>
    /// <param name="value">The <see cref="ListBuilder"/> value.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the memory.</returns>
    public static ComponentValue CreateList(ListBuilder value, bool externallyOwned) => new(value, externallyOwned);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a enum value.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The enum value.</param>
    /// <param name="toBytes">A function pointer to convert the enum to a <see cref="ByteVector"/>.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the enum.</returns>
    public static unsafe ComponentValue CreateEnum<T>(
        T value,
        delegate* managed<T, ByteVector> toBytes) where T : struct, Enum
    {
        var bytes = toBytes(value);
        var val = new wasmtime_component_val();
        val.kind = 17;
        val.of.enumeration = bytes.Value;

        return new ComponentValue(val, externallyOwned: false);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> from a enum value.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The enum value.</param>
    /// <param name="toBytes">A function pointer to convert the enum to a <see cref="ByteVector"/>.</param>
    /// <param name="expand">Expands the enum to its constituent values.</param>
    /// <returns>A <see cref="ComponentValue"/> representing the enum.</returns>
    public static unsafe ComponentValue CreateFlags<T>(
        T value,
        delegate* managed<T, ByteVector> toBytes,
        delegate* managed<T, Span<T>, int> expand
    ) where T : unmanaged, Enum
    {
        Span<T> values = stackalloc T[64];
        var count = expand(value, values);

        var val = new wasmtime_component_val();
        val.kind = 20;

        if (count != 0)
        {
            var builder = new FlagsBuilder(count, disposeValues: false);
            for (var i = 0; i < count; i++)
            {
                var bytes = toBytes(values[i]);
                builder[i] = bytes;
            }

            val.of.flags = builder.Value;
        }

        return new ComponentValue(val, externallyOwned: false);
    }

    public readonly bool ToBoolean()
    {
        if (_val.kind != 0) ThrowInvalidKind(_val.kind, "Boolean");
        return _val.of.boolean != 0;
    }

    public readonly sbyte ToSByte()
    {
        if (_val.kind != 1) ThrowInvalidKind(_val.kind, "SByte");
        return _val.of.s8;
    }

    public readonly byte ToByte()
    {
        if (_val.kind != 2) ThrowInvalidKind(_val.kind, "Byte");
        return _val.of.u8;
    }

    public readonly short ToInt16()
    {
        if (_val.kind != 3) ThrowInvalidKind(_val.kind, "Int16");
        return _val.of.s16;
    }

    public readonly ushort ToUInt16()
    {
        if (_val.kind != 4) ThrowInvalidKind(_val.kind, "UInt16");
        return _val.of.u16;
    }

    public readonly int ToInt32()
    {
        if (_val.kind != 5) ThrowInvalidKind(_val.kind, "Int32");
        return _val.of.s32;
    }

    public readonly uint ToUInt32()
    {
        if (_val.kind != 6) ThrowInvalidKind(_val.kind, "UInt32");
        return _val.of.u32;
    }

    public readonly long ToInt64()
    {
        if (_val.kind != 7) ThrowInvalidKind(_val.kind, "Int64");
        return _val.of.s64;
    }

    public readonly ulong ToUInt64()
    {
        if (_val.kind != 8) ThrowInvalidKind(_val.kind, "UInt64");
        return _val.of.u64;
    }

    public readonly float ToFloat()
    {
        if (_val.kind != 9) ThrowInvalidKind(_val.kind, "Float");
        return _val.of.f32;
    }

    public readonly double ToDouble()
    {
        if (_val.kind != 10) ThrowInvalidKind(_val.kind, "Double");
        return _val.of.f64;
    }

    public readonly char ToChar()
    {
        if (_val.kind != 11) ThrowInvalidKind(_val.kind, "Char");
        return (char)_val.of.character;
    }

    public readonly string ToStringValue()
    {
        if (_val.kind != 12) ThrowInvalidKind(_val.kind, "String");
        return new ByteVector(_val.of.@string).GetString();
    }

    public readonly ListBuilder ToListBuilder()
    {
        if (_val.kind != 13) ThrowInvalidKind(_val.kind, "List");
        return new ListBuilder(_val.of.list);
    }

    public readonly RecordBuilder ToRecordBuilder()
    {
        if (_val.kind != 14) ThrowInvalidKind(_val.kind, "Record");
        return new RecordBuilder(_val.of.record);
    }

    public readonly unsafe T ToEnum<T>(delegate* managed<ByteVector, T> toBytes) where T : struct, Enum
    {
        if (_val.kind != 17) ThrowInvalidKind(_val.kind, "Enum");
        return toBytes(new ByteVector(_val.of.enumeration));
    }

    public readonly unsafe T ToFlags<T>(
        delegate* managed<ByteVector, T> toEnum,
        delegate* managed<ReadOnlySpan<T>, T> combine) where T : unmanaged, Enum
    {
        if (_val.kind != 20) ThrowInvalidKind(_val.kind, "Flags");
        var flags = new FlagsBuilder(_val.of.flags);

        switch (flags.Length)
        {
            case 0:
                return default;
            case 1:
                return toEnum(flags[0]);
            default:
            {
                Span<T> enums = stackalloc T[64];

                for (var i = 0; i < flags.Length; i++)
                {
                    enums[i] = toEnum(new ByteVector(flags.Value.data[i]));
                }

                return combine(enums.Slice(0, flags.Length));
            }
        }
    }

    public unsafe ComponentCallResults ToTuple()
    {
        if (_val.kind != 15) ThrowInvalidKind(_val.kind, "Tuple");
        var tuple = _val.of.tuple;

        return new ComponentCallResults(tuple.data, (int)tuple.size);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing a tuple type (kind 15).
    /// </summary>
    /// <remarks>
    /// The <paramref name="elements"/> are <em>consumed</em>: each element's contents are moved into
    /// the tuple's native storage. The caller must not dispose the elements; disposing the returned
    /// value frees them.
    /// </remarks>
    public static unsafe ComponentValue CreateTuple(ReadOnlySpan<ComponentValue> elements)
    {
        var val = new wasmtime_component_val();
        val.kind = 15;

        wasmtime_component_valtuple tuple;
        wasmtime_component_valtuple_new_uninit(&tuple, (UIntPtr)elements.Length);

        var data = (ComponentValue*)tuple.data;
        for (var i = 0; i < elements.Length; i++)
        {
            data[i] = elements[i];
        }

        val.of.tuple = tuple;
        return new ComponentValue(val, false);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing an option type.
    /// Pass a non-null inner value for Some, or null for None.
    /// </summary>
    public static unsafe ComponentValue CreateOption(ComponentValue? inner)
    {
        var val = new wasmtime_component_val();
        val.kind = 18;
        if (inner.HasValue)
        {
            var src = inner.Value._val;
            var ptr = wasmtime_component_val_new(&src);
            val.of.option = ptr;
        }
        else
        {
            val.of.option = null;
        }
        return new ComponentValue(val, false);
    }

    /// <summary>
    /// Extracts the inner value of an option. Returns null for None.
    /// </summary>
    public readonly unsafe ComponentValue? ToOption()
    {
        if (_val.kind != 18) ThrowInvalidKind(_val.kind, "Option");
        if (_val.of.option == null) return null;
        return new ComponentValue(*_val.of.option, true);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing a variant type.
    /// </summary>
    /// <param name="discriminant">The variant case name.</param>
    /// <param name="payload">The payload value, or null if the case has no payload.</param>
    public static unsafe ComponentValue CreateVariant(string discriminant, ComponentValue? payload)
    {
        return CreateVariant(new ByteVector(discriminant), payload, copyDiscriminant: false);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing a variant type from a pre-encoded discriminant.
    /// When <paramref name="copyDiscriminant"/> is false, the ByteVector's data is used directly (for cached constants).
    /// When true, a copy is made so the original ByteVector remains valid.
    /// </summary>
    public static unsafe ComponentValue CreateVariant(ByteVector discriminant, ComponentValue? payload, bool copyDiscriminant)
    {
        var val = new wasmtime_component_val();
        val.kind = 16;
        val.of.variant.discriminant = copyDiscriminant ? new ByteVector(discriminant).Value : discriminant.Value;
        if (payload.HasValue)
        {
            var src = payload.Value._val;
            var ptr = wasmtime_component_val_new(&src);
            val.of.variant.val = ptr;
        }
        else
        {
            val.of.variant.val = null;
        }
        return new ComponentValue(val, false);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing an owned resource handle.
    /// </summary>
    /// <param name="context">The store context.</param>
    /// <param name="rep">The host representation value (handle index).</param>
    /// <param name="typeId">The resource type ID as registered with the linker.</param>
    public static unsafe ComponentValue CreateOwnResource(StoreContext context, uint rep, uint typeId)
    {
        var hostRes = wasmtime_component_resource_host_new(true, rep, typeId);
        try
        {
            wasmtime_component_resource_any_t* anyRes;
            var error = wasmtime_component_resource_host_to_any(context.Handle, hostRes, &anyRes);
            WasmtimeException.ThrowIfError(error);

            var val = new wasmtime_component_val();
            val.kind = 21;
            val.of.resource = anyRes;
            return new ComponentValue(val, false);
        }
        finally
        {
            wasmtime_component_resource_host_delete(hostRes);
        }
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing an owned resource handle.
    /// Overload accepting a <see cref="Store"/> for use outside of callbacks.
    /// </summary>
    public static unsafe ComponentValue CreateOwnResource(Store store, uint rep, uint typeId)
        => CreateOwnResource(new StoreContext(store.Context), rep, typeId);

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing a borrowed resource handle.
    /// </summary>
    /// <param name="context">The store context.</param>
    /// <param name="rep">The host representation value (handle index).</param>
    /// <param name="typeId">The resource type ID as registered with the linker.</param>
    public static unsafe ComponentValue CreateBorrowResource(StoreContext context, uint rep, uint typeId)
    {
        var hostRes = wasmtime_component_resource_host_new(false, rep, typeId);
        try
        {
            wasmtime_component_resource_any_t* anyRes;
            var error = wasmtime_component_resource_host_to_any(context.Handle, hostRes, &anyRes);
            WasmtimeException.ThrowIfError(error);
            var val = new wasmtime_component_val();
            val.kind = 21;
            val.of.resource = anyRes;
            return new ComponentValue(val, false);
        }
        finally
        {
            wasmtime_component_resource_host_delete(hostRes);
        }
    }

    /// <summary>
    /// Extracts the host representation (u32 handle) from a resource ComponentValue.
    /// Requires a store context to convert from any-resource to host-resource.
    /// Also accepts u32 values (kind 6) since borrow handles may arrive as plain u32
    /// from the native wasmtime layer.
    /// </summary>
    public readonly unsafe uint ToResourceRep(StoreContext context)
    {
        // Borrow handles may arrive as plain u32 from the native layer
        if (_val.kind == 6)
        {
            return _val.of.u32;
        }

        if (_val.kind != 21) ThrowInvalidKind(_val.kind, "Resource");
        wasmtime_component_resource_host_t* hostRes;
        var error = wasmtime_component_resource_any_to_host(context.Handle, _val.of.resource, &hostRes);
        WasmtimeException.ThrowIfError(error);
        var rep = wasmtime_component_resource_host_rep(hostRes);
        wasmtime_component_resource_host_delete(hostRes);
        return rep;
    }

    /// <summary>
    /// Extracts the host representation and drops the underlying resource handle.
    /// Intended for use by [resource-drop] imports to clear the wasmtime handle table.
    /// </summary>
    public readonly unsafe uint ToResourceRepAndDrop(StoreContext context)
    {
        // Borrow handles may arrive as plain u32 from the native layer
        if (_val.kind == 6)
        {
            return _val.of.u32;
        }

        if (_val.kind != 21) ThrowInvalidKind(_val.kind, "Resource");
        wasmtime_component_resource_host_t* hostRes;
        var error = wasmtime_component_resource_any_to_host(context.Handle, _val.of.resource, &hostRes);
        WasmtimeException.ThrowIfError(error);
        var rep = wasmtime_component_resource_host_rep(hostRes);
        wasmtime_component_resource_host_delete(hostRes);

        if (_val.of.resource != null)
        {
            var dropError = wasmtime_component_resource_any_drop(context.Handle, _val.of.resource);
            WasmtimeException.ThrowIfError(dropError);
        }

        return rep;
    }

    /// <summary>
    /// Extracts the host representation (u32 handle) from a resource ComponentValue.
    /// Overload accepting a <see cref="Store"/> for use outside of callbacks.
    /// </summary>
    public readonly unsafe uint ToResourceRep(Store store)
        => ToResourceRep(new StoreContext(store.Context));

    /// <summary>
    /// Drops an owned resource handle (kind 21): runs the owning component's destructor and frees
    /// the host-side handle memory, then clears this value. Use for <c>own&lt;resource&gt;</c> handles
    /// returned from a component (no host-resource conversion is involved, unlike
    /// <see cref="ToResourceRepAndDrop"/>).
    /// </summary>
    public unsafe void DropResource(StoreContext context)
    {
        if (_val.kind != 21)
        {
            throw new InvalidOperationException($"Cannot drop ComponentValue of kind {_val.kind} as a resource.");
        }

        if (_val.of.resource != null)
        {
            // any_drop performs component-model cleanup (incl. the guest destructor); any_delete
            // then frees the host-side handle memory. Both are required per the wasmtime C API.
            var error = wasmtime_component_resource_any_drop(context.Handle, _val.of.resource);
            WasmtimeException.ThrowIfError(error);
            wasmtime_component_resource_any_delete(_val.of.resource);
        }

        _val = default;
    }

    /// <summary>
    /// Extracts the discriminant and payload of a variant.
    /// </summary>
    public readonly unsafe (string Discriminant, ComponentValue? Payload) ToVariant()
    {
        if (_val.kind != 16) ThrowInvalidKind(_val.kind, "Variant");
        var discriminant = new ByteVector(_val.of.variant.discriminant).GetString();
        ComponentValue? payload = _val.of.variant.val != null
            ? new ComponentValue(*_val.of.variant.val, true)
            : null;
        return (discriminant, payload);
    }

    /// <summary>
    /// Creates a <see cref="ComponentValue"/> representing a result type (kind 19).
    /// </summary>
    /// <remarks>
    /// The <paramref name="payload"/> is <em>consumed</em>: its contents are moved onto a native
    /// heap allocation owned by the returned value (see <c>wasmtime_component_val_new</c>). The
    /// caller must not dispose <paramref name="payload"/>; disposing the returned value frees it.
    /// </remarks>
    /// <param name="isOk">True for the <c>ok</c> arm, false for the <c>err</c> arm.</param>
    /// <param name="payload">The arm payload, or null when that arm carries no value.</param>
    public static unsafe ComponentValue CreateResult(bool isOk, ComponentValue? payload)
    {
        var val = new wasmtime_component_val();
        val.kind = 19;
        val.of.result.is_ok = isOk ? (byte)1 : (byte)0;
        if (payload.HasValue)
        {
            var src = payload.Value._val;
            val.of.result.val = wasmtime_component_val_new(&src);
        }
        else
        {
            val.of.result.val = null;
        }
        return new ComponentValue(val, false);
    }

    /// <summary>
    /// Extracts the discriminant and payload of a result.
    /// </summary>
    public readonly unsafe (bool IsOk, ComponentValue? Payload) ToResult()
    {
        if (_val.kind != 19) throw new InvalidOperationException($"Cannot convert ComponentValue of kind {_val.kind} to Result.");
        var isOk = _val.of.result.is_ok != 0;
        ComponentValue? payload = _val.of.result.val != null
            ? new ComponentValue(*_val.of.result.val, true)
            : null;
        return (isOk, payload);
    }

    /// <summary>
    /// Extracts the discriminant and payload of a variant without allocating a string.
    /// The discriminant is returned as a raw ByteVector for comparison against cached constants.
    /// </summary>
    public readonly unsafe (ByteVector Discriminant, ComponentValue? Payload) ToVariantRaw()
    {
        if (_val.kind != 16) ThrowInvalidKind(_val.kind, "Variant");
        var discriminant = new ByteVector(_val.of.variant.discriminant);
        ComponentValue? payload = _val.of.variant.val != null
            ? new ComponentValue(*_val.of.variant.val, true)
            : null;
        return (discriminant, payload);
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidKind(byte kind, string target)
        => throw new InvalidOperationException($"Cannot convert ComponentValue of kind {kind} to {target}.");

    /// <inheritdoc />
    public unsafe void Dispose()
    {
        Dispose(ref _val);
    }

    /// <summary>
    /// Releases resources associated with this value using a store context.
    /// This enables proper cleanup for borrowed resources.
    /// </summary>
    public unsafe void Dispose(Store store)
    {
        Dispose(ref _val, store.Context);
    }

    internal static unsafe void Dispose(ref wasmtime_component_val val, wasmtime_context* context)
    {
        if (context != null)
        {
            DropBorrowedResources(ref val, context);
        }

        Dispose(ref val);
    }

    internal static unsafe void Dispose(ref wasmtime_component_val val)
    {
        switch (val.kind)
        {
            case 0:
                return;
            case 12:
                new ByteVector(val.of.@string).Dispose();
                DecrementActiveCount();
                break;
            case 13:
                new ListBuilder(val.of.list).Dispose();
                DecrementActiveCount();
                break;
            case 14:
                new RecordBuilder(val.of.record).Dispose();
                DecrementActiveCount();
                break;
            case 15:
            {
                var data = (wasmtime_component_val*)val.of.tuple.data;
                var size = (int)val.of.tuple.size;
                for (var i = 0; i < size; i++)
                {
                    Dispose(ref data[i]);
                }
                fixed (wasmtime_component_valtuple* t = &val.of.tuple)
                {
                    wasmtime_component_valtuple_delete(t);
                }
                DecrementActiveCount();
                break;
            }
            case 16:
                new ByteVector(val.of.variant.discriminant).Dispose();
                if (val.of.variant.val != null)
                {
                    wasmtime_component_val_free(val.of.variant.val);
                }
                DecrementActiveCount();
                break;
            case 17:
                // Enums are not disposed since the values are cached and reused (constants).
                DecrementActiveCount();
                break;
            case 18:
                if (val.of.option != null)
                {
                    wasmtime_component_val_free(val.of.option);
                }
                DecrementActiveCount();
                break;
            case 19: // result
                if (val.of.result.val != null)
                {
                    wasmtime_component_val_free(val.of.result.val);
                }
                DecrementActiveCount();
                break;
            case 20:
                new FlagsBuilder(val.of.flags).Dispose();
                DecrementActiveCount();
                break;
            case 21:
                if (val.of.resource != null)
                {
                    wasmtime_component_resource_any_delete(val.of.resource);
                }
                DecrementActiveCount();
                break;
        }

        val = default;
    }

    internal static unsafe void DropBorrowedResources(ref wasmtime_component_val val, wasmtime_context* context)
    {
        switch (val.kind)
        {
            case 13: // list
            {
                var list = val.of.list;
                var size = (int)list.size;
                for (var i = 0; i < size; i++)
                {
                    DropBorrowedResources(ref list.data[i], context);
                }
                break;
            }
            case 14: // record
            {
                var record = val.of.record;
                var size = (int)record.size;
                for (var i = 0; i < size; i++)
                {
                    DropBorrowedResources(ref record.data[i].val, context);
                }
                break;
            }
            case 15: // tuple
            {
                var tuple = val.of.tuple;
                var size = (int)tuple.size;
                for (var i = 0; i < size; i++)
                {
                    DropBorrowedResources(ref tuple.data[i], context);
                }
                break;
            }
            case 16: // variant
                if (val.of.variant.val != null)
                {
                    DropBorrowedResources(ref *val.of.variant.val, context);
                }
                break;
            case 18: // option
                if (val.of.option != null)
                {
                    DropBorrowedResources(ref *val.of.option, context);
                }
                break;
            case 19: // result
                if (val.of.result.val != null)
                {
                    DropBorrowedResources(ref *val.of.result.val, context);
                }
                break;
            case 21: // resource
                if (val.of.resource != null && !wasmtime_component_resource_any_owned(val.of.resource))
                {
                    var error = wasmtime_component_resource_any_drop(context, val.of.resource);
                    WasmtimeException.ThrowIfError(error);
                }
                break;
        }
    }

    /// <summary>
    /// Creates a ComponentValue containing a list of records where each record has
    /// homogeneous blittable primitive fields, constructed from a flat data span.
    /// Uses [SuppressGCTransition] P/Invoke variants for minimal per-call overhead.
    /// </summary>
    /// <typeparam name="T">The primitive type (float, double, int, etc.).</typeparam>
    /// <param name="result">Pointer to the output ComponentValue slot.</param>
    /// <param name="flatData">Flat span of primitive values (recordCount * fieldCount elements).</param>
    /// <param name="recordCount">Number of records in the list.</param>
    /// <param name="fieldCount">Number of fields per record.</param>
    /// <param name="fieldNameTemplates">Template ByteVectors for field names (copied per record).</param>
    /// <param name="primitiveKind">The wasmtime_component_valkind_t for the primitive type.</param>
    public static unsafe void CreateListOfBlittableRecords<T>(
        ComponentValue* result,
        ReadOnlySpan<T> flatData,
        int recordCount,
        int fieldCount,
        ReadOnlySpan<ByteVector> fieldNameTemplates,
        byte primitiveKind)
        where T : unmanaged
    {
        wasmtime_component_vallist list;
        wasmtime_component_vallist_new_uninit_fast(&list, (UIntPtr)recordCount);

        for (int i = 0; i < recordCount; i++)
        {
            wasmtime_component_valrecord rec;
            wasmtime_component_valrecord_new_uninit_fast(&rec, (UIntPtr)fieldCount);

            for (int f = 0; f < fieldCount; f++)
            {
                // Use pointer arithmetic to avoid managed ref / fixed statement overhead
                var entryPtr = rec.data + f;
                var template = fieldNameTemplates[f];

                // Copy field name from template (wasmtime takes ownership and frees after processing)
                wasm_byte_vec_new_fast(&entryPtr->name, template.Value.size, template.Value.data);

                // Write primitive value directly to the union (all fields at offset 0)
                entryPtr->val.kind = primitiveKind;
                *(T*)&entryPtr->val.of = flatData[i * fieldCount + f];
            }

            var listEntry = list.data + i;
            listEntry->kind = 14; // WASMTIME_COMPONENT_RECORD
            listEntry->of.record = rec;
        }

        result->_val.kind = 13; // WASMTIME_COMPONENT_LIST
        result->_val.of.list = list;
    }

    /// <summary>
    /// Extracts a list of homogeneous blittable primitives (list&lt;u8&gt;, list&lt;f32&gt;, …) into
    /// <paramref name="dst"/> in one strided pass over the native value array. Read-direction mirror
    /// of <see cref="CreateListOfBlittableRecords{T}"/>: avoids the per-element managed ListBuilder
    /// indexer (each access copies a tagged-union ComponentValue by value) plus the per-element kind
    /// check. The element kind is guaranteed by the WIT list type, so it is trusted here. All scalar
    /// kinds live at union offset 0, so the reinterpret reads the same field as ToByte/ToInt32/etc.
    /// </summary>
    /// <typeparam name="T">The primitive type (byte, ushort, int, float, …).</typeparam>
    /// <param name="list">The native list to read from (not consumed; wasmtime owns it).</param>
    /// <param name="dst">Destination span, sized to <paramref name="list"/>.Length by the caller.</param>
    public static unsafe void ReadListOfPrimitives<T>(in ListBuilder list, Span<T> dst)
        where T : unmanaged
    {
        var src = (ComponentValue*)list.Value.data;
        var n = list.Length;
        for (var i = 0; i < n; i++)
            dst[i] = *(T*)&src[i]._val.of;
    }
}
