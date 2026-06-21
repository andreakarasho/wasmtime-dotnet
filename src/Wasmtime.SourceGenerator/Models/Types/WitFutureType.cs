namespace Wasmtime.SourceGenerator.Models;

/// <summary>
/// Represents a future type in WIT (component-model async).
/// </summary>
public record WitFutureType(
    WitType ElementType
) : WitType(WitTypeKind.Future);
