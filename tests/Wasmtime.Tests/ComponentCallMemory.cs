using System;
using System.Threading.Tasks;
using Xunit;

namespace Wasmtime.Tests;

[CollectionDefinition("Memory", DisableParallelization = true)]
public class ComponentCallMemory(ComponentFixture fixture, ITestOutputHelper output)
{
    // WASM in 32-bit mode has a 4 GB memory limit
    // We call 5 GB in total to validate that the memory is properly released between calls.

    private const int Size = 100 * 1024 * 1024; // 100 MB
    private const int Iterations = 50; // 100 * 100 MB = 5 GB total

    [Fact]
    public void Host_To_Guest()
    {
        using var state = fixture.CreateState();

        var str = new string('a', Size);

        for (var i = 1; i <= Iterations; i++)
        {
            state.Exports.AcceptString(str);

            if (i % 10 == 0)
            {
                output.WriteLine($"Iteration {i}");
            }
        }
    }

    [Fact]
    public void Guest_To_Host()
    {
        using var state = fixture.CreateState();

        for (var i = 1; i <= Iterations; i++)
        {
            state.Exports.ReturnString(Size);

            if (i % 10 == 0)
            {
                output.WriteLine($"Iteration {i}");
            }
        }
    }

    // Guards against the host-side leak of composite return values: wasmtime lowers returned
    // strings/lists/records/... into the embedder-owned results array and post_return does NOT
    // free those host copies — only wasmtime_component_val_delete does. Measures HOST private
    // bytes (not guest wasm memory, which the test above covers). The returned managed string is
    // discarded each iteration so GC keeps the managed side flat; only a native leak shows.
    [Fact]
    public void Guest_To_Host_DoesNotLeakHostMemory()
    {
        using var state = fixture.CreateState();

        const int sz = 10 * 1024 * 1024; // 10 MB per returned string
        const int iters = 300;           // ~3000 MB leaked if returned vals are never freed

        for (var i = 0; i < 5; i++) state.Exports.ReturnString(sz); // warmup

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();
        long before = proc.PrivateMemorySize64;

        for (var i = 0; i < iters; i++) state.Exports.ReturnString(sz);

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        proc.Refresh();
        long after = proc.PrivateMemorySize64;

        long deltaMb = (after - before) / (1024 * 1024);
        output.WriteLine($"Private bytes before={before / 1048576}MB after={after / 1048576}MB delta={deltaMb}MB (full leak would be ~{(long)iters * sz / 1048576}MB)");

        Assert.True(deltaMb < 500, $"Host private bytes grew {deltaMb}MB across {iters} composite-returning calls — returned values are leaking.");
    }
}
