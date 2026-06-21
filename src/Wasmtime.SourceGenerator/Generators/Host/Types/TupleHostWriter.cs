using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class TupleHostWriter(EquatableArray<WitType> elementTypes) : TypeHostWriter(WitTypeKind.Tuple)
{
    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        if (elementTypes.Length == 0)
        {
            throw new InvalidOperationException("Tuples with zero elements are not supported.");
        }

        sb.Append('(');
        for (var i = 0; i < elementTypes.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            elementTypes[i].HostWriter.WriteCSharpType(sb, resolver);
        }
        sb.Append(')');
    }

    /// <inheritdoc />
    public override void WriteValueGetterInitializer(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append("global::Wasmtime.ComponentCallResults ").Append(uniqueName).Append(" = ").Append(paramName).AppendLine(".ToTuple();");
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append('(');
        sb.IncrementIndent();

        for (var i = 0; i < elementTypes.Length; i++)
        {
            sb.AppendLine(i > 0 ? ", " : "");
            elementTypes[i].HostWriter.WriteResultGetter(sb, uniqueName, i, resolver);
        }

        sb.DecrementIndent();
        sb.AppendLine();
        sb.Append(')');
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        // Build a tuple ComponentValue from the C# ValueTuple's elements (.Item1, .Item2, ...).
        // CreateTuple consumes each element, so emit them with ignoreDispose: true.
        // Array-backed (not stackalloc): ComponentValue holds native pointers, which .NET
        // Framework's stackalloc->Span path rejects.
        sb.Append("global::Wasmtime.ComponentValue.CreateTuple(new global::Wasmtime.ComponentValue[] { ");
        for (var i = 0; i < elementTypes.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            elementTypes[i].HostWriter.WriteComponentValue(sb, $"{paramKey}.Item{i + 1}", ignoreDispose: true, resolver, externallyOwned);
        }
        sb.Append(" })");
    }
}
