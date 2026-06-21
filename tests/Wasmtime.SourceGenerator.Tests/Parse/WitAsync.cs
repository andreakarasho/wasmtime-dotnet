using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Tests;

public class WitAsync
{
    [Fact]
    public void Future()
    {
        var type = ParseParamType("future<u32>");

        var future = Assert.IsType<WitFutureType>(type);
        Assert.Equal(WitTypeKind.U32, future.ElementType.Kind);
    }

    [Fact]
    public void Stream()
    {
        var type = ParseParamType("stream<u8>");

        var stream = Assert.IsType<WitStreamType>(type);
        Assert.Equal(WitTypeKind.U8, stream.ElementType.Kind);
    }

    private static WitType ParseParamType(string typeStr)
    {
        var file = Wit.Parse(
            $$"""
            package test;

            world World {
                export test: func(a: {{typeStr}});
            }
            """);

        var package = Assert.Single(file.Packages.Values);
        var version = Assert.Single(package.Versions.Values);
        var world = Assert.Single(version.Worlds.Values);
        var item = Assert.Single(world.Definitions.Items);
        var export = Assert.IsType<WitWorldExport>(item);
        var func = Assert.IsType<WitFuncType>(export.Type);

        return Assert.Single(func.Parameters).Type;
    }
}
