using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class ResourceHostWriter(
    WitPackageNameVersion package,
    string name
) : TypeHostWriter(WitTypeKind.Resource)
{
    /// <summary>
    /// When true, resource types emit as <c>uint</c> and use raw handle-based ComponentValue creation.
    /// Set during export generation where nested class types are not in scope.
    /// </summary>
    [ThreadStatic]
    public static bool ExportContext;

    /// <summary>
    /// The resource type ID assigned during linker registration.
    /// Must be set before code generation.
    /// </summary>
    public uint TypeId { get; set; }

    /// <summary>
    /// The C# class name for this resource (e.g., "System", "EntityCommands").
    /// Set during resource processing in WriteResourceImports.
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// The handle table field name on the parent imports class (e.g., "_systemHandles").
    /// </summary>
    public string? HandleTableField { get; set; }

    /// <summary>
    /// The handle counter field name on the parent imports class (e.g., "_nextSystemHandle").
    /// </summary>
    public string? HandleCounterField { get; set; }

    /// <summary>
    /// The private store helper method name (e.g., "StoreSystem").
    /// Stores an object in the handle table and returns its handle.
    /// </summary>
    public string? StoreMethodName { get; set; }

    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        if (!ExportContext && ClassName != null)
        {
            sb.Append(ClassName);
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
        if (!ExportContext && HandleTableField != null)
        {
            sb.AppendLine();
            sb.AppendLine("#if DEBUG");
            sb.Append("@this.Get").Append(ClassName!).Append("(").Append(paramName).Append(".ToResourceRep(context))");
            sb.AppendLine();
            sb.AppendLine("#else");
            sb.Append("@this.").Append(HandleTableField).Append("[").Append(paramName).Append(".ToResourceRep(context)]");
            sb.AppendLine();
            sb.Append("#endif");
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
        if (!ExportContext && StoreMethodName != null)
        {
            // Use the store helper to inline: store object → get handle → create ComponentValue
            sb.Append("global::Wasmtime.ComponentValue.CreateOwnResource(context, @this.")
                .Append(StoreMethodName).Append("(").Append(paramKey).Append("), ").Append(TypeId).Append(")");
        }
        else
        {
            sb.Append("global::Wasmtime.ComponentValue.CreateOwnResource(context, ").Append(paramKey).Append(", ").Append(TypeId).Append(")");
        }
    }
}
