using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class BorrowHostWriter(
    WitType elementType
) : TypeHostWriter(WitTypeKind.Borrow)
{
    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        var rhw = ResolveResourceHostWriter(resolver);
        if (!ResourceHostWriter.ExportContext && rhw?.ClassName != null)
        {
            sb.Append(rhw.ClassName);
        }
        else
        {
            sb.Append("uint");
        }
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        var rhw = ResolveResourceHostWriter(resolver);
        if (!ResourceHostWriter.ExportContext && rhw?.HandleTableField != null)
        {
            sb.Append("@this.").Append(rhw.HandleTableField).Append("[").Append(paramName).Append(".ToResourceRep(context)]");
        }
        else
        {
            sb.Append(paramName).Append(".ToResourceRep(context)");
        }
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        // Borrows should rarely be created in return values, but if needed, preserve the resource type ID when available.
        var typeId = ResolveResourceTypeId(resolver);
        sb.Append("global::Wasmtime.ComponentValue.CreateBorrowResource(context, ").Append(paramKey).Append(", ").Append(typeId).Append(")");
    }

    private ResourceHostWriter? ResolveResourceHostWriter(ITypeContainerResolver resolver)
    {
        var resolved = elementType;
        while (resolved is WitCustomType customType)
        {
            resolved = customType.Resolve(resolver);
        }

        if (resolved is WitResourceType resourceType && resourceType.HostWriter is ResourceHostWriter rhw)
        {
            return rhw;
        }

        return null;
    }

    private uint ResolveResourceTypeId(ITypeContainerResolver resolver)
    {
        return ResolveResourceHostWriter(resolver)?.TypeId ?? 0;
    }
}
