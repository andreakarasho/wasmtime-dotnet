using Wasmtime.SourceGenerator.Generators.Host;

namespace Wasmtime.SourceGenerator.Models;

/// <summary>
/// Represents a result type in WIT with no result type.
/// </summary>
public record WitResultNoResultType(
    WitType ErrType
) : WitType(WitTypeKind.Result)
{
    public override TypeHostWriter HostWriter => new ResultHostWriter(null, ErrType);
}