using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class ListHostWriter(WitType ElementType) : TypeHostWriter(WitTypeKind.List)
{
    public override bool MustBeDisposed => true;

    /// <summary>
    /// Returns true if the element type is a blittable primitive (excludes bool, char, enum, string, records, resources).
    /// Blittable primitives can use stackalloc/ArrayPool to avoid heap allocations.
    /// </summary>
    private bool IsBlittablePrimitive => ElementType.Kind is
        WitTypeKind.U8 or WitTypeKind.S8 or
        WitTypeKind.U16 or WitTypeKind.S16 or
        WitTypeKind.U32 or WitTypeKind.S32 or
        WitTypeKind.U64 or WitTypeKind.S64 or
        WitTypeKind.F32 or WitTypeKind.F64;

    /// <summary>
    /// Returns true if the element type is a non-blittable reference type that can use ArrayPool.
    /// This covers strings, records, lists, variants, options — any type where new T[] can be pooled.
    /// </summary>
    private bool IsPoolableReferenceType => !IsBlittablePrimitive;

    // ─── Blittable record fast-path detection ──────────────────────────

    private record BlittableRecordInfo(int FieldCount, WitTypeKind PrimitiveKind, EquatableArray<WitField> Fields);

    /// <summary>
    /// If the element type resolves to a record where ALL fields share the same blittable
    /// primitive type (e.g. record { x: f32, y: f32 }), returns info for generating a
    /// MemoryMarshal.Cast fast path instead of per-element RecordBuilder marshaling.
    /// </summary>
    private BlittableRecordInfo? TryGetBlittableRecord(ITypeContainerResolver resolver)
    {
        var elementType = ElementType;

        if (elementType is WitCustomType customType)
            elementType = customType.Resolve(resolver);

        if (elementType is not WitRecordType recordType || recordType.Fields.Length == 0)
            return null;

        WitTypeKind? commonKind = null;

        foreach (var field in recordType.Fields)
        {
            var fieldType = field.Type;
            if (fieldType is WitCustomType fieldCustom)
                fieldType = fieldCustom.Resolve(resolver);

            var kind = fieldType.Kind;

            if (kind is not (WitTypeKind.U8 or WitTypeKind.S8 or
                WitTypeKind.U16 or WitTypeKind.S16 or
                WitTypeKind.U32 or WitTypeKind.S32 or
                WitTypeKind.U64 or WitTypeKind.S64 or
                WitTypeKind.F32 or WitTypeKind.F64))
            {
                return null;
            }

            commonKind ??= kind;
            if (commonKind != kind)
                return null;
        }

        return new BlittableRecordInfo(recordType.Fields.Length, commonKind!.Value, recordType.Fields);
    }

    private static string GetPrimitiveCSharpType(WitTypeKind kind) => kind switch
    {
        WitTypeKind.U8 => "byte",
        WitTypeKind.S8 => "sbyte",
        WitTypeKind.U16 => "ushort",
        WitTypeKind.S16 => "short",
        WitTypeKind.U32 => "uint",
        WitTypeKind.S32 => "int",
        WitTypeKind.U64 => "ulong",
        WitTypeKind.S64 => "long",
        WitTypeKind.F32 => "float",
        WitTypeKind.F64 => "double",
        _ => throw new NotSupportedException($"Not a blittable primitive: {kind}")
    };

    private static string GetComponentValueExtractMethod(WitTypeKind kind) => kind switch
    {
        WitTypeKind.U8 => "ToByte",
        WitTypeKind.S8 => "ToSByte",
        WitTypeKind.U16 => "ToUInt16",
        WitTypeKind.S16 => "ToInt16",
        WitTypeKind.U32 => "ToUInt32",
        WitTypeKind.S32 => "ToInt32",
        WitTypeKind.U64 => "ToUInt64",
        WitTypeKind.S64 => "ToInt64",
        WitTypeKind.F32 => "ToFloat",
        WitTypeKind.F64 => "ToDouble",
        _ => throw new NotSupportedException($"Not a blittable primitive: {kind}")
    };

    private static string GetCreateComponentValueMethod(WitTypeKind kind) => kind switch
    {
        WitTypeKind.U8 => "CreateByte",
        WitTypeKind.S8 => "CreateSByte",
        WitTypeKind.U16 => "CreateUInt16",
        WitTypeKind.S16 => "CreateInt16",
        WitTypeKind.U32 => "CreateUInt32",
        WitTypeKind.S32 => "CreateInt32",
        WitTypeKind.U64 => "CreateUInt64",
        WitTypeKind.S64 => "CreateInt64",
        WitTypeKind.F32 => "CreateFloat",
        WitTypeKind.F64 => "CreateDouble",
        _ => throw new NotSupportedException($"Not a blittable primitive: {kind}")
    };

    /// <summary>
    /// Maps WitTypeKind to the native wasmtime_component_valkind_t byte value.
    /// </summary>
    private static byte GetNativePrimitiveKind(WitTypeKind kind) => kind switch
    {
        WitTypeKind.Bool => 0,
        WitTypeKind.S8 => 1,
        WitTypeKind.U8 => 2,
        WitTypeKind.S16 => 3,
        WitTypeKind.U16 => 4,
        WitTypeKind.S32 => 5,
        WitTypeKind.U32 => 6,
        WitTypeKind.S64 => 7,
        WitTypeKind.U64 => 8,
        WitTypeKind.F32 => 9,
        WitTypeKind.F64 => 10,
        _ => throw new NotSupportedException($"Not a blittable primitive: {kind}")
    };

    /// <inheritdoc />
    public override void WriteParameter(IndentedStringBuilder sb, string name, ITypeContainerResolver resolver)
    {
        // Use ReadOnlySpan<T> for method parameters to avoid unnecessary allocations.
        sb.Append("global::System.ReadOnlySpan<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append("> ").Append(name);
    }

    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append("[]");
    }

    /// <inheritdoc />
    public override void WriteReturnType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        // Use ReadOnlySpan<T> for return types — allows the host to return a span
        // over existing storage (e.g. SoA arrays in an ECS) without allocation.
        sb.Append("global::System.ReadOnlySpan<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(">");
    }

    /// <inheritdoc />
    public override void WriteParameterInitializer(
        IndentedStringBuilder sb,
        string name,
        ITypeContainerResolver resolver,
        bool ignoreDispose,
        bool externallyOwned)
    {
        var blittable = TryGetBlittableRecord(resolver);
        if (blittable != null)
        {
            WriteParameterInitializerBlittable(sb, name, resolver, ignoreDispose, externallyOwned, blittable);
            return;
        }

        var builderName = $"builder_{name.ToSafeVariable()}";
        var indexName = $"i_{name.ToSafeVariable()}";
        var itemName = $"{name}[{indexName}]";

        sb.AppendLine("// Convert array to list builder");

        if (!ignoreDispose)
        {
            sb.Append("using ");
        }

        sb.Append("global::Wasmtime.ListBuilder ").Append(builderName).Append(" = new global::Wasmtime.ListBuilder(").Append(name).AppendLine(".Length);");
        sb.Append("for (int ").Append(indexName).Append(" = 0; ").Append(indexName).Append(" < ").Append(name).Append(".Length; ").Append(indexName).AppendLine("++)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        ElementType.HostWriter.WriteParameterInitializer(sb, itemName, resolver, ignoreDispose: true, externallyOwned: externallyOwned);

        sb.Append(builderName).Append("[").Append(indexName).Append("] = ");
        ElementType.HostWriter.WriteComponentValue(sb, itemName, ignoreDispose: true, resolver: resolver, externallyOwned: externallyOwned);

        sb.AppendLine(";");
        sb.DecrementIndent();
        sb.AppendLine("}");
    }

    /// <inheritdoc />
    public override void WriteParameterSetter(IndentedStringBuilder sb, string parametersVariable, string name,
        int startIndex, bool ignoreDispose, ITypeContainerResolver resolver, bool externallyOwned)
    {
        var blittable = TryGetBlittableRecord(resolver);
        if (blittable != null)
        {
            WriteParameterSetterBlittable(sb, parametersVariable, name, startIndex, blittable);
            return;
        }

        sb.Append(parametersVariable).Append("[").Append(startIndex).Append("] = global::Wasmtime.ComponentValue.CreateList(builder_")
            .Append(name.ToSafeVariable())
            .Append(", externallyOwned: ")
            .Append(externallyOwned ? "true" : "false")
            .AppendLine(");");
    }

    /// <inheritdoc />
    public override void WriteComponentValue(IndentedStringBuilder sb, string name, bool ignoreDispose,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        sb.Append("global::Wasmtime.ComponentValue.CreateList(builder_").Append(name.ToSafeVariable())
            .Append(", externallyOwned: ")
            .Append(externallyOwned ? "true" : "false")
            .Append(")");
    }

    /// <summary>
    /// Phase 2 fast path: for blittable records in the return direction (host → WASM),
    /// uses MemoryMarshal.Cast to get a flat primitive span. The actual list creation
    /// is deferred to WriteParameterSetterBlittable which calls the batch native method.
    /// </summary>
    private void WriteParameterInitializerBlittable(
        IndentedStringBuilder sb,
        string name,
        ITypeContainerResolver resolver,
        bool ignoreDispose,
        bool externallyOwned,
        BlittableRecordInfo blittable)
    {
        var flatName = $"{name.ToSafeVariable()}_flat";
        var primType = GetPrimitiveCSharpType(blittable.PrimitiveKind);

        sb.AppendLine("// Blittable record batch fast path — MemoryMarshal.Cast + SuppressGCTransition native calls");

        // ReadOnlySpan<float> flat = MemoryMarshal.Cast<RecordType, float>(name);
        sb.Append("global::System.ReadOnlySpan<").Append(primType).Append("> ").Append(flatName)
            .Append(" = global::System.Runtime.InteropServices.MemoryMarshal.Cast<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(", ").Append(primType).Append(">(").Append(name).AppendLine(");");
    }

    /// <summary>
    /// Phase 2 fast path setter: calls ComponentValue.CreateListOfBlittableRecords which
    /// uses [SuppressGCTransition] P/Invoke variants and writes directly to native memory,
    /// avoiding per-record managed wrapper overhead.
    /// </summary>
    private void WriteParameterSetterBlittable(
        IndentedStringBuilder sb,
        string parametersVariable,
        string name,
        int startIndex,
        BlittableRecordInfo blittable)
    {
        var flatName = $"{name.ToSafeVariable()}_flat";
        var primType = GetPrimitiveCSharpType(blittable.PrimitiveKind);
        var nativeKind = GetNativePrimitiveKind(blittable.PrimitiveKind);

        // ComponentValue.CreateListOfBlittableRecords<float>(
        //     &results[0], flatData, recordCount, fieldCount,
        //     stackalloc ByteVector[] { Constants.X, Constants.Y }, 9);
        sb.Append("global::Wasmtime.ComponentValue.CreateListOfBlittableRecords<").Append(primType).AppendLine(">(");
        sb.IncrementIndent();

        sb.Append("(global::Wasmtime.ComponentValue*)&").Append(parametersVariable)
            .Append("[").Append(startIndex).AppendLine("],");
        sb.Append(flatName).AppendLine(",");
        sb.Append(name).AppendLine(".Length,");
        sb.Append(blittable.FieldCount).AppendLine(",");

        // stackalloc ByteVector[] { Constants.Field1, Constants.Field2, ... }
        sb.Append("stackalloc global::Wasmtime.ByteVector[] { ");
        for (int f = 0; f < blittable.FieldCount; f++)
        {
            if (f > 0) sb.Append(", ");
            sb.Append("global::Wit.Constants.").Append(blittable.Fields[f].CSharpName);
        }
        sb.AppendLine(" },");
        sb.Append(nativeKind).AppendLine(");");

        sb.DecrementIndent();
    }

    /// <inheritdoc />
    public override void WriteResultGetterInitializer(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        var parameterName = $"{paramName}_{index}";
        var builderName = $"{parameterName}_b";

        sb.Append("global::Wasmtime.ListBuilder ").Append(builderName).Append(" = ")
            .Append(paramName).Append("[").Append(index).AppendLine("].ToListBuilder();");

        var indexName = $"{parameterName}_i";

        // Result is exposed as ReadOnlySpan<T> over a heap array. Pooled/stackalloc storage
        // must NOT be used here: the span is returned to (or retained by) the caller, so a
        // span over method-local memory would dangle once this method returns.
        sb.AppendLine("// Convert list builder to array");
        WriteToArray(sb, resolver, parameterName, builderName, indexName, usePooledSpan: false, usePooledArray: false);
    }

    /// <inheritdoc />
    public override void WriteResultCleanup(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        // Heap-backed result array (see WriteResultGetterInitializer) — nothing to return to a pool.
    }

    /// <inheritdoc />
    public override void WriteResultGetter(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        sb.Append(paramName).Append('_').Append(index);
    }

    public override void WriteValueGetterInitializer(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append("global::Wasmtime.ListBuilder builder_").Append(uniqueName).Append(" = ").Append(paramName).Append(".ToListBuilder();").AppendLine();

        WriteToArray(sb, resolver, uniqueName, "builder_" + uniqueName, "i", usePooledArray: false);
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append(uniqueName);
    }

    private void WriteToArray(
        IndentedStringBuilder sb,
        ITypeContainerResolver resolver,
        string parameterName,
        string builderName,
        string indexName,
        bool usePooledSpan = false,
        bool usePooledArray = false)
    {
        var countName = $"{parameterName}_len";
        var rentedName = $"{parameterName}_rented";

        sb.Append("var ").Append(countName).Append(" = ").Append(builderName).AppendLine(".Length;");

        if (usePooledSpan)
        {
            // Use stackalloc for small lists, ArrayPool for large — avoid heap allocation
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[]? ").Append(rentedName).AppendLine(" = null;");
            sb.Append("global::System.Span<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("> ").Append(parameterName).Append(" = ").Append(countName).AppendLine(" <= 128");
            sb.IncrementIndent();
            sb.Append("? stackalloc ");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[").Append(countName).AppendLine("]");
            sb.Append(": new global::System.Span<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append(">(").Append(rentedName).Append(" = global::System.Buffers.ArrayPool<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append(">.Shared.Rent(").Append(countName).Append("), 0, ").Append(countName).AppendLine(");");
            sb.DecrementIndent();
        }
        else if (usePooledArray)
        {
            // Use ArrayPool for non-blittable types to reduce GC pressure on large lists.
            // ArrayPool.Rent may return a larger array, so we slice via Span to the exact count.
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[]? ").Append(rentedName).AppendLine(" = null;");
            sb.Append("global::System.Span<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("> ").Append(parameterName).Append(" = ").Append(countName).AppendLine(" <= 128");
            sb.IncrementIndent();
            sb.Append("? new ");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[").Append(countName).AppendLine("]");
            sb.Append(": new global::System.Span<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append(">(").Append(rentedName).Append(" = global::System.Buffers.ArrayPool<");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append(">.Shared.Rent(").Append(countName).Append("), 0, ").Append(countName).AppendLine(");");
            sb.DecrementIndent();
        }
        else
        {
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[] ").Append(parameterName).Append(" = new ");
            ElementType.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append("[").Append(countName).AppendLine("];");
        }

        sb.Append("for (int ").Append(indexName).Append(" = 0; ").Append(indexName).Append(" < ").Append(countName).Append("; ").Append(indexName).AppendLine("++)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        ElementType.HostWriter.WriteValueGetterInitializer(sb, $"{builderName}[{indexName}]", $"{parameterName}_{indexName}", resolver);
        sb.Append(parameterName).Append("[").Append(indexName).Append("] = ");
        ElementType.HostWriter.WriteValueGetter(sb, $"{builderName}[{indexName}]", $"{parameterName}_{indexName}", resolver);
        sb.AppendLine(";");
        sb.DecrementIndent();
        sb.AppendLine("}");
    }

    /// <summary>
    /// Emits code to return pooled array to ArrayPool if it was rented.
    /// Call after the span has been consumed. For blittable value types.
    /// </summary>
    private void WritePooledReturn(IndentedStringBuilder sb, ITypeContainerResolver resolver, string parameterName)
    {
        var rentedName = $"{parameterName}_rented";
        sb.Append("if (").Append(rentedName).Append(" != null) global::System.Buffers.ArrayPool<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(">.Shared.Return(").Append(rentedName).AppendLine(");");
    }

    /// <summary>
    /// Emits code to return pooled array to ArrayPool for reference types.
    /// Uses clearArray: true to avoid holding references in the pool.
    /// </summary>
    private void WritePooledReturnReference(IndentedStringBuilder sb, ITypeContainerResolver resolver, string parameterName)
    {
        var rentedName = $"{parameterName}_rented";
        sb.Append("if (").Append(rentedName).Append(" != null) global::System.Buffers.ArrayPool<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(">.Shared.Return(").Append(rentedName).AppendLine(", clearArray: true);");
    }

    /// <summary>
    /// Phase 1 fast path: extracts primitives from RecordBuilders into a flat stackalloc/ArrayPool
    /// span, then reinterprets as ReadOnlySpan&lt;RecordType&gt; via MemoryMarshal.Cast.
    /// </summary>
    private void WriteToArrayBlittable(
        IndentedStringBuilder sb,
        ITypeContainerResolver resolver,
        string parameterName,
        string builderName,
        BlittableRecordInfo blittable)
    {
        var countName = $"{parameterName}_len";
        var flatLenName = $"{parameterName}_flat_len";
        var flatName = $"{parameterName}_flat";
        var rentedName = $"{parameterName}_rented";
        var indexName = $"{parameterName}_i";
        var recName = $"{parameterName}_rec";
        var primType = GetPrimitiveCSharpType(blittable.PrimitiveKind);
        var extractMethod = GetComponentValueExtractMethod(blittable.PrimitiveKind);

        sb.AppendLine("// Convert list of records to span via MemoryMarshal.Cast (blittable fast path)");

        // var count = builder.Length;
        sb.Append("var ").Append(countName).Append(" = ").Append(builderName).AppendLine(".Length;");

        // var flatLen = count * fieldCount;
        sb.Append("var ").Append(flatLenName).Append(" = ").Append(countName)
            .Append(" * ").Append(blittable.FieldCount).AppendLine(";");

        // float[]? rented = null;
        sb.Append(primType).Append("[]? ").Append(rentedName).AppendLine(" = null;");

        // Span<float> flat = flatLen <= 512 ? stackalloc float[flatLen] : ArrayPool...
        sb.Append("global::System.Span<").Append(primType).Append("> ").Append(flatName)
            .Append(" = ").Append(flatLenName).AppendLine(" <= 512");
        sb.IncrementIndent();
        sb.Append("? stackalloc ").Append(primType).Append("[").Append(flatLenName).AppendLine("]");
        sb.Append(": new global::System.Span<").Append(primType).Append(">(")
            .Append(rentedName).Append(" = global::System.Buffers.ArrayPool<").Append(primType)
            .Append(">.Shared.Rent(").Append(flatLenName).Append("), 0, ").Append(flatLenName).AppendLine(");");
        sb.DecrementIndent();

        // for (int i = 0; i < count; i++)
        sb.Append("for (int ").Append(indexName).Append(" = 0; ").Append(indexName)
            .Append(" < ").Append(countName).Append("; ").Append(indexName).AppendLine("++)");
        sb.AppendLine("{");
        sb.IncrementIndent();

        // var rec = builder[i].ToRecordBuilder();
        sb.Append("var ").Append(recName).Append(" = ").Append(builderName)
            .Append("[").Append(indexName).AppendLine("].ToRecordBuilder();");

        // flat[i * N + f] = rec.Get(f).ToFloat();
        for (int f = 0; f < blittable.FieldCount; f++)
        {
            sb.Append(flatName).Append("[").Append(indexName).Append(" * ").Append(blittable.FieldCount)
                .Append(" + ").Append(f).Append("] = ").Append(recName).Append(".Get(").Append(f)
                .Append(").").Append(extractMethod).AppendLine("();");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");

        // ReadOnlySpan<RecordType> result = MemoryMarshal.Cast<float, RecordType>(flat);
        sb.Append("global::System.ReadOnlySpan<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append("> ").Append(parameterName)
            .Append(" = global::System.Runtime.InteropServices.MemoryMarshal.Cast<")
            .Append(primType).Append(", ");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(">(").Append(flatName).AppendLine(");");
    }

    /// <summary>
    /// Emits code to return a primitive-typed pooled array used by the blittable record fast path.
    /// </summary>
    private static void WritePooledReturnPrimitive(IndentedStringBuilder sb, string parameterName, WitTypeKind primitiveKind)
    {
        var rentedName = $"{parameterName}_rented";
        var primType = GetPrimitiveCSharpType(primitiveKind);
        sb.Append("if (").Append(rentedName).Append(" != null) global::System.Buffers.ArrayPool<")
            .Append(primType).Append(">.Shared.Return(").Append(rentedName).AppendLine(");");
    }
}
