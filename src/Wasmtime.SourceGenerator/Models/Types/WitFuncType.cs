namespace Wasmtime.SourceGenerator.Models;

/// <summary>
/// Represents a function type in WIT.
/// </summary>
public record WitFuncType(
    EquatableArray<WitFuncParameter> Parameters,
    EquatableArray<WitType> Results
) : WitType(WitTypeKind.Func);

/// <summary>
/// Represents a function parameter in WIT.
/// </summary>
public record WitFuncParameter(
    string Name,
    WitType Type
)
{
    // Opt-in span fast path: a `string` param named *-utf8 surfaces host-side as
    // ReadOnlySpan<byte> over the raw canonical-ABI UTF-8 bytes instead of allocating a
    // managed string per call (hot per-frame byte channels; see Utf8SpanHostWriter).
    public WitType Type { get; } =
        Type.Kind == WitTypeKind.String && Name.EndsWith("-utf8", StringComparison.Ordinal)
            ? new WitUtf8StringType()
            : Type;

    public string CSharpName { get; } = StringUtils.GetName(Name);

    public string CSharpVariableName { get; } = StringUtils.GetName(Name, uppercaseFirst: false);
}