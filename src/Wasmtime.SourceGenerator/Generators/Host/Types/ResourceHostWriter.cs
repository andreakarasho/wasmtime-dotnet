using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class ResourceHostWriter(
    WitPackageNameVersion package,
    string name
) : TypeHostWriter(WitTypeKind.Resource)
{
    /// <summary>
    /// The resource type ID assigned during linker registration.
    /// Must be set before code generation.
    /// </summary>
    public uint TypeId { get; set; }

    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        sb.Append("uint");
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        sb.Append(paramName).Append(".ToResourceRep(context)");
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        sb.Append("global::Wasmtime.ComponentValue.CreateOwnResource(context, ").Append(paramKey).Append(", ").Append(TypeId).Append(")");
    }
}
