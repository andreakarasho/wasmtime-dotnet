using Wasmtime.SourceGenerator.Generators.Host;

namespace Wasmtime.SourceGenerator.Models;

/// <summary>
/// Represents a WIT type.
/// </summary>
/// <param name="Kind">The kind of WIT type.</param>
public record WitType(WitTypeKind Kind)
{
    public static WitType Bool { get; } = new(WitTypeKind.Bool);
    public static WitType U8 { get; } = new(WitTypeKind.U8);
    public static WitType U16 { get; } = new(WitTypeKind.U16);
    public static WitType U32 { get; } = new(WitTypeKind.U32);
    public static WitType U64 { get; } = new(WitTypeKind.U64);
    public static WitType S8 { get; } = new(WitTypeKind.S8);
    public static WitType S16 { get; } = new(WitTypeKind.S16);
    public static WitType S32 { get; } = new(WitTypeKind.S32);
    public static WitType S64 { get; } = new(WitTypeKind.S64);
    public static WitType F32 { get; } = new(WitTypeKind.F32);
    public static WitType F64 { get; } = new(WitTypeKind.F64);
    public static WitType Char { get; } = new(WitTypeKind.Char);
    public static WitType String { get; } = new(WitTypeKind.String);
    public static WitType EmptyResult { get; } = new WitResultEmptyType();

    public virtual TypeHostWriter HostWriter => new(Kind);
}

/// <summary>
/// A WIT <c>string</c> that surfaces host-side as <c>ReadOnlySpan&lt;byte&gt;</c> over the raw
/// canonical-ABI UTF-8 bytes (no per-call string alloc). Substituted for parameters whose WIT
/// name ends in <c>-utf8</c> — see <see cref="WitFuncParameter"/>.
/// </summary>
public record WitUtf8StringType() : WitType(WitTypeKind.String)
{
    public override TypeHostWriter HostWriter => new Utf8SpanHostWriter();
}