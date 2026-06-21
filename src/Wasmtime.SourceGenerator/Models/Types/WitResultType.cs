using Wasmtime.SourceGenerator.Generators.Host;

namespace Wasmtime.SourceGenerator.Models;

/// <summary>
/// Represents a result type in WIT.
/// </summary>
public record WitResultType(
    WitType OkType,
    WitType ErrType
) : WitType(WitTypeKind.Result)
{
    public override TypeHostWriter HostWriter => new ResultHostWriter(OkType, ErrType);
}

/// <summary>
/// Represents a bare <c>result</c> type in WIT (no ok or err payload).
/// </summary>
public record WitResultEmptyType() : WitType(WitTypeKind.Result)
{
    public override TypeHostWriter HostWriter => new ResultHostWriter(null, null);
}