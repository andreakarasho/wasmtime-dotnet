using Wasmtime.SourceGenerator.Generators.Host;

namespace Wasmtime.SourceGenerator.Models;

public record WitResourceType(
    WitPackageNameVersion Package,
    string Name,
    EquatableArray<WitField> Fields
) : WitType(WitTypeKind.Resource)
{
    private ResourceHostWriter? _hostWriter;

    public override TypeHostWriter HostWriter => _hostWriter ??= new ResourceHostWriter(Package, Name);
}
