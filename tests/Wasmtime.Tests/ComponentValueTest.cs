using System;
using Xunit;

namespace Wasmtime.Tests;

/// <summary>
/// Value-layer tests for <see cref="ComponentValue"/> that exercise native marshalling
/// directly (no wasm component required).
///
/// Note: <see cref="ComponentValue.CreateResult"/> (like CreateOption/CreateVariant)
/// *consumes* its payload — the payload's contents are moved onto a native heap
/// allocation, so the payload must NOT be disposed by the caller. Likewise the payload
/// returned from <see cref="ComponentValue.ToResult"/> is a view into the parent and is
/// freed when the parent is disposed.
/// </summary>
public class ComponentValueTest
{
    [Fact]
    public void Result_Ok_WithPayload_Roundtrips()
    {
        var inner = ComponentValue.CreateInt32(42); // moved into result; do not dispose
        using var result = ComponentValue.CreateResult(isOk: true, inner);

        var (isOk, payload) = result.ToResult();

        Assert.True(isOk);
        Assert.NotNull(payload);
        Assert.Equal(42, payload!.Value.ToInt32());
    }

    [Fact]
    public void Result_Err_WithPayload_Roundtrips()
    {
        var inner = ComponentValue.CreateString("boom", externallyOwned: false); // moved into result
        using var result = ComponentValue.CreateResult(isOk: false, inner);

        var (isOk, payload) = result.ToResult();

        Assert.False(isOk);
        Assert.NotNull(payload);
        Assert.Equal("boom", payload!.Value.ToStringValue());
    }

    [Fact]
    public void Result_Ok_NoPayload_Roundtrips()
    {
        using var result = ComponentValue.CreateResult(isOk: true, null);

        var (isOk, payload) = result.ToResult();

        Assert.True(isOk);
        Assert.Null(payload);
    }

    [Fact]
    public void Result_Err_NoPayload_Roundtrips()
    {
        using var result = ComponentValue.CreateResult(isOk: false, null);

        var (isOk, payload) = result.ToResult();

        Assert.False(isOk);
        Assert.Null(payload);
    }

    [Fact]
    public void Tuple_Roundtrips()
    {
        var a = ComponentValue.CreateInt32(7);                              // moved into tuple
        var b = ComponentValue.CreateString("hi", externallyOwned: false); // moved into tuple
        // Array-backed span (not stackalloc): ComponentValue holds native pointers, which
        // .NET Framework's stackalloc->Span path rejects.
        using var tuple = ComponentValue.CreateTuple(new ComponentValue[] { a, b });

        using var results = tuple.ToTuple();

        Assert.Equal(2, results.Length);
        Assert.Equal(7, results[0].ToInt32());
        Assert.Equal("hi", results[1].ToStringValue());
    }

    [Fact]
    public void ResultStruct_Ok_ExposesValue_NotError()
    {
        var r = Result<int, string>.Ok(5);

        Assert.True(r.IsOk);
        Assert.Equal(5, r.Value);
        Assert.Throws<InvalidOperationException>(() => r.Error);
    }

    [Fact]
    public void ResultStruct_Err_ExposesError_NotValue()
    {
        var r = Result<int, string>.Err("nope");

        Assert.False(r.IsOk);
        Assert.Equal("nope", r.Error);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }
}
