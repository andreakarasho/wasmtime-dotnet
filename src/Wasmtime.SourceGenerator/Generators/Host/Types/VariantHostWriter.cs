using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public class VariantHostWriter(WitPackageNameVersion package, string name, EquatableArray<WitVariantCase> cases) : TypeHostWriter(WitTypeKind.Variant)
{
    private bool HasPayloads => cases.Any(c => c.Type != null);

    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        sb.Append("global::");
        package.PackageName.WritePath(sb);
        sb.Append('.').Append(name);
    }

    /// <inheritdoc />
    public override void WriteResultGetterInitializer(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        if (HasPayloads)
        {
            // For variants with payloads, create a temp variable
            var safeName = $"{paramName}_{index}".ToSafeVariable();
            sb.Append("var varRaw_").Append(safeName).Append(" = ").Append(paramName).Append("[").Append(index).AppendLine("].ToVariant();");

            // Build the variant struct from discriminant + payload
            WriteCSharpType(sb, resolver);
            sb.Append(" varVal_").Append(safeName).AppendLine(";");
            WriteVariantStructConstruction(sb, $"varRaw_{safeName}", $"varVal_{safeName}", resolver);
        }
    }

    /// <inheritdoc />
    public override void WriteResultGetter(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        if (HasPayloads)
        {
            var safeName = $"{paramName}_{index}".ToSafeVariable();
            sb.Append("varVal_").Append(safeName);
        }
        else
        {
            WriteValueGetter(sb, $"{paramName}[{index}]", $"{paramName}_{index}", resolver);
        }
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        if (!HasPayloads)
        {
            // Simple enum variant — use the enum helper FromByteVector
            sb.Append("global::");
            package.PackageName.WritePath(sb);
            sb.Append('.').Append(name).Append("Helper.FromByteVector(new global::Wasmtime.ByteVector(");
            sb.Append(paramName).Append(".ToVariant().Discriminant))");
        }
        else
        {
            // Reference the variable created by WriteValueGetterInitializer
            sb.Append(uniqueName);
        }
    }

    /// <inheritdoc />
    public override void WriteValueGetterInitializer(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        if (HasPayloads)
        {
            sb.Append("var varRaw_").Append(uniqueName).Append(" = ").Append(paramName).AppendLine(".ToVariant();");
            WriteCSharpType(sb, resolver);
            sb.Append(" ").Append(uniqueName).AppendLine(";");
            WriteVariantStructConstruction(sb, $"varRaw_{uniqueName}", uniqueName, resolver);
        }
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        if (!HasPayloads)
        {
            sb.Append("global::Wasmtime.ComponentValue.CreateVariant(");
            sb.Append("global::");
            package.PackageName.WritePath(sb);
            sb.Append('.').Append(name).Append("Helper.ToByteVector(");
            sb.Append(paramKey).Append(").GetString(), null)");
        }
        else
        {
            // For variants with payloads, generate a switch on the discriminant
            sb.Append("global::Wasmtime.ComponentValue.CreateVariant(\"unknown\", null)");
        }
    }

    private void WriteVariantStructConstruction(
        IndentedStringBuilder sb,
        string rawVarName,
        string resultVarName,
        ITypeContainerResolver resolver)
    {
        // rawVarName is a (string Discriminant, ComponentValue? Payload) tuple
        // resultVarName is the target variant struct variable

        sb.Append("switch (").Append(rawVarName).AppendLine(".Discriminant)");
        sb.AppendLine("{");
        sb.IncrementIndent();

        foreach (var caseItem in cases)
        {
            var caseName = StringUtils.GetName(caseItem.Name);
            sb.Append("case \"").Append(caseItem.Name).AppendLine("\":");
            sb.IncrementIndent();

            if (caseItem.Type != null)
            {
                sb.Append(resultVarName).Append(" = ");
                WriteCSharpType(sb, resolver);
                sb.Append(".Create").Append(caseName).Append("(");
                // Extract payload value using the payload's ComponentValue
                var payloadExpr = $"{rawVarName}.Payload!.Value";
                caseItem.Type.HostWriter.WriteValueGetter(sb, payloadExpr, payloadExpr, resolver);
                sb.AppendLine(");");
            }
            else
            {
                sb.Append(resultVarName).Append(" = ");
                WriteCSharpType(sb, resolver);
                sb.Append(".Create").Append(caseName).AppendLine("();");
            }

            sb.AppendLine("break;");
            sb.DecrementIndent();
        }

        sb.AppendLine("default:");
        sb.IncrementIndent();
        sb.Append("throw new global::System.InvalidOperationException($\"Unknown variant discriminant: {").Append(rawVarName).AppendLine(".Discriminant}\");");
        sb.DecrementIndent();

        sb.DecrementIndent();
        sb.AppendLine("}");
    }
}
