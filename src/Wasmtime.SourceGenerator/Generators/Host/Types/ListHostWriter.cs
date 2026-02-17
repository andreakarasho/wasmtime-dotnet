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

    /// <inheritdoc />
    public override void WriteResultGetterInitializer(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        var parameterName = $"{paramName}_{index}";
        var builderName = $"{parameterName}_b";
        var indexName = $"{parameterName}_i";

        sb.AppendLine(IsBlittablePrimitive
            ? "// Convert list builder to span (pooled — avoids heap allocation)"
            : "// Convert list builder to array");
        sb.Append("global::Wasmtime.ListBuilder ").Append(builderName).Append(" = ")
            .Append(paramName).Append("[").Append(index).AppendLine("].ToListBuilder();");

        WriteToArray(sb, resolver, parameterName, builderName, indexName, usePooledSpan: IsBlittablePrimitive);
    }

    /// <inheritdoc />
    public override void WriteResultCleanup(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        if (!IsBlittablePrimitive) return;

        var parameterName = $"{paramName}_{index}";
        WritePooledReturn(sb, resolver, parameterName);
    }

    /// <inheritdoc />
    public override void WriteResultGetter(IndentedStringBuilder sb, string paramName, int index, ITypeContainerResolver resolver)
    {
        sb.Append(paramName).Append('_').Append(index);
    }

    public override void WriteValueGetterInitializer(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append("global::Wasmtime.ListBuilder builder_").Append(uniqueName).Append(" = ").Append(paramName).Append(".ToListBuilder();").AppendLine();

        WriteToArray(sb, resolver, uniqueName, "builder_" + uniqueName, "i");
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
        bool usePooledSpan = false)
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
    /// Call after the span has been consumed.
    /// </summary>
    private void WritePooledReturn(IndentedStringBuilder sb, ITypeContainerResolver resolver, string parameterName)
    {
        var rentedName = $"{parameterName}_rented";
        sb.Append("if (").Append(rentedName).Append(" != null) global::System.Buffers.ArrayPool<");
        ElementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append(">.Shared.Return(").Append(rentedName).AppendLine(");");
    }
}
