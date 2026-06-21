using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

/// <summary>
/// Host writer for WIT <c>result&lt;ok, err&gt;</c> (and its no-ok / no-err / bare forms).
/// Maps to <c>global::Wasmtime.Result&lt;TOk, TErr&gt;</c>, substituting
/// <c>global::Wasmtime.Unit</c> for an absent arm payload.
/// </summary>
public class ResultHostWriter(WitType? okType, WitType? errType) : TypeHostWriter(WitTypeKind.Result)
{
    public override bool MustBeDisposed => true;

    /// <inheritdoc />
    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        sb.Append("global::Wasmtime.Result<");
        WriteArmType(sb, okType, resolver);
        sb.Append(", ");
        WriteArmType(sb, errType, resolver);
        sb.Append('>');
    }

    private static void WriteArmType(IndentedStringBuilder sb, WitType? armType, ITypeContainerResolver resolver)
    {
        if (armType is null)
        {
            sb.Append("global::Wasmtime.Unit");
        }
        else
        {
            armType.HostWriter.WriteCSharpType(sb, resolver);
        }
    }

    /// <inheritdoc />
    public override void WriteResultGetterInitializer(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        var safeName = $"{paramName}_{index}".ToSafeVariable();

        // var res_X = result[index].ToResult();
        sb.Append("var res_").Append(safeName).Append(" = ").Append(paramName).Append("[").Append(index).AppendLine("].ToResult();");

        // Result<OK,ERR> resval_X = res_X.IsOk ? Result<OK,ERR>.Ok(<ok payload>) : Result<OK,ERR>.Err(<err payload>);
        WriteCSharpType(sb, resolver);
        sb.Append(" resval_").Append(safeName).Append(" = res_").Append(safeName).Append(".IsOk ? ");
        WriteArmConstruct(sb, "Ok", okType, $"res_{safeName}.Payload!.Value", resolver);
        sb.Append(" : ");
        WriteArmConstruct(sb, "Err", errType, $"res_{safeName}.Payload!.Value", resolver);
        sb.AppendLine(";");
    }

    private void WriteArmConstruct(IndentedStringBuilder sb, string method, WitType? armType, string payloadExpr,
        ITypeContainerResolver resolver)
    {
        WriteCSharpType(sb, resolver);
        sb.Append('.').Append(method).Append('(');
        if (armType is null)
        {
            sb.Append("global::Wasmtime.Unit.Value");
        }
        else
        {
            armType.HostWriter.WriteValueGetter(sb, payloadExpr, "result_arm", resolver);
        }
        sb.Append(')');
    }

    /// <inheritdoc />
    public override void WriteResultGetter(IndentedStringBuilder sb, string paramName, int index,
        ITypeContainerResolver resolver)
    {
        var safeName = $"{paramName}_{index}".ToSafeVariable();
        sb.Append("resval_").Append(safeName);
    }

    /// <inheritdoc />
    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName,
        ITypeContainerResolver resolver)
    {
        // Fallback for nested usage (e.g. list<result<...>>). Mirrors OptionHostWriter's inline
        // approach; full nested support would need an initializer like WriteResultGetterInitializer.
        sb.Append(paramName).Append(".ToResult()");
    }

    /// <inheritdoc />
    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        sb.Append("global::Wasmtime.ComponentValue.CreateResult(").Append(paramKey).Append(".IsOk, ");
        sb.Append(paramKey).Append(".IsOk ? ");
        WriteArmPayload(sb, okType, $"{paramKey}.Value", resolver, externallyOwned);
        sb.Append(" : ");
        WriteArmPayload(sb, errType, $"{paramKey}.Error", resolver, externallyOwned);
        sb.Append(')');
    }

    private void WriteArmPayload(IndentedStringBuilder sb, WitType? armType, string valueExpr,
        ITypeContainerResolver resolver, bool externallyOwned)
    {
        if (armType is null)
        {
            sb.Append("(global::Wasmtime.ComponentValue?)null");
            return;
        }

        sb.Append("(global::Wasmtime.ComponentValue?)(");
        armType.HostWriter.WriteComponentValue(sb, valueExpr, ignoreDispose: true, resolver, externallyOwned);
        sb.Append(')');
    }
}
