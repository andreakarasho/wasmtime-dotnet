using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class OptionHostWriter(WitType elementType) : TypeHostWriter(WitTypeKind.Option)
{
    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        elementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append('?');
    }

    /// <inheritdoc />
    public override void WriteResultGetterInitializer(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        // Create a temp variable for the option extraction:
        // var opt_args_2 = args[2].ToOption();
        // uint? optVal_args_2 = opt_args_2.HasValue ? opt_args_2.Value.ToUInt32() : null;
        var safeName = $"{paramName}_{index}".ToSafeVariable();

        sb.Append("var opt_").Append(safeName).Append(" = ").Append(paramName).Append("[").Append(index).AppendLine("].ToOption();");

        // Declare the typed nullable variable
        elementType.HostWriter.WriteCSharpType(sb, resolver);
        sb.Append("? optVal_").Append(safeName).Append(" = opt_").Append(safeName);
        sb.Append(".HasValue ? ");
        elementType.HostWriter.WriteValueGetter(sb, $"opt_{safeName}.Value", safeName, resolver);
        sb.AppendLine(" : null;");
    }

    /// <inheritdoc />
    public override void WriteResultGetter(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        var safeName = $"{paramName}_{index}".ToSafeVariable();
        sb.Append("optVal_").Append(safeName);
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        // Fallback for non-indexed usage: assumes paramName is already a ComponentValue
        sb.Append(paramName).Append(".ToOption()");
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        // For reference types (e.g., resource classes in import context), the nullable is a
        // reference type annotation (T?) not Nullable<T>, so we use the variable directly
        // instead of .Value which only exists on Nullable<T> value types.
        var isRefType = IsResourceClassType(elementType, resolver);
        var valueKey = isRefType ? paramKey : paramKey + ".Value";

        sb.Append("global::Wasmtime.ComponentValue.CreateOption(");
        sb.Append(paramKey).Append(" != null ? ");
        elementType.HostWriter.WriteComponentValue(sb, valueKey, ignoreDispose: true, resolver, externallyOwned);
        sb.Append(" : (global::Wasmtime.ComponentValue?)null)");
    }

    private static bool IsResourceClassType(WitType type, ITypeContainerResolver resolver)
    {
        var resolved = type;
        while (resolved is WitCustomType customType)
            resolved = customType.Resolve(resolver);

        return resolved is WitResourceType resourceType
            && resourceType.HostWriter is ResourceHostWriter { ClassName: not null }
            && !ResourceHostWriter.ExportContext;
    }
}
