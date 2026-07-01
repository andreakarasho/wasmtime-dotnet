using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Wasmtime.Interop;

namespace Wasmtime;

public sealed unsafe class Store : IDisposable
{
    private readonly ConcurrentDictionary<nint, ComponentInstance> _instances = new();

    internal wasmtime_store* Handle;
    internal wasmtime_context* Context;
    internal bool Disposed;

    private wasi_config_t* _wasiP2Config;

    public Store(Engine engine)
    {
        Handle = wasmtime_store_new(engine.Handle, null, IntPtr.Zero);
        Context = wasmtime_store_context(Handle);
    }

    internal bool IsWasiP2Added => _wasiP2Config != null;

    public void AddWasiP2(bool inheritStdin = false, bool inheritStdout = false, bool inheritStderr = false,
                          (string HostPath, string GuestPath)[]? preopenDirs = null,
                          (string HostPath, string GuestPath)[]? writableDirs = null)
    {
        var cfg = wasi_config_new();

        if (inheritStdin) wasi_config_inherit_stdin(cfg);
        if (inheritStdout) wasi_config_inherit_stdout(cfg);
        if (inheritStderr) wasi_config_inherit_stderr(cfg);

        // WASI defaults to an empty argv; the .NET wasi startup needs argv[0].
        wasi_config_set_argv(cfg, 1, new[] { "guest" });

        // Dir + file perms bitmask: READ=1, WRITE=2.
        const nuint read = 1, readWrite = 3;

        if (preopenDirs != null)
        {
            // Read-only grant — e.g. the UO data dir (the guest only reads asset files).
            foreach (var (host, guest) in preopenDirs)
                wasi_config_preopen_dir(cfg, host, guest, read, read);
        }

        if (writableDirs != null)
        {
            // Read+write grant — e.g. the guest's config dir (settings/profile/gumps
            // persistence). Scope this to a dedicated dir, never the whole filesystem.
            foreach (var (host, guest) in writableDirs)
                wasi_config_preopen_dir(cfg, host, guest, readWrite, readWrite);
        }

        wasmtime_context_set_wasi(Context, cfg);

        _wasiP2Config = cfg;
    }

    private void ReleaseUnmanagedResources()
    {
        if (Handle != null)
        {
            wasmtime_store_delete(Handle);
            Handle = null;
        }
    }

    /// <summary>
    /// Gets an instance of a component associated with this store.
    /// </summary>
    /// <param name="component">The component to instantiate.</param>
    /// <param name="linker">The linker to use for instantiation.</param>
    /// <returns>The instantiated component instance.</returns>
    public ComponentInstance GetComponentInstance(Component component, Linker linker)
    {
#if NET
        ObjectDisposedException.ThrowIf(Disposed, nameof(Store));
#else
        if (Disposed) throw new ObjectDisposedException(nameof(Store));
#endif

        return _instances.TryGetValue((nint)component.Handle, out var existing)
            ? existing
            : GetComponentInstanceInner(component, linker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ComponentInstance GetComponentInstanceInner(Component component, Linker linker)
    {
        if (linker.IsWasiP2Added != IsWasiP2Added)
        {
            throw new WasmtimeException("WASI P2 must be added to both the linker and the store");
        }

        wasmtime_component_instance handle;
        var error = wasmtime_component_linker_instantiate(linker.ComponentHandle, Context, component.Handle, &handle);

        WasmtimeException.ThrowIfError(error);

        var instance = new ComponentInstance(component, handle, this);
        _instances.TryAdd((nint)component.Handle, instance);
        return instance;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Verify no component calls are in progress. Since stores are single-threaded,
        // an in-progress call here means the caller has a bug (disposing mid-call).
        foreach (var instance in _instances.Values)
        {
            if (instance.InCall)
            {
                throw new InvalidOperationException(
                    "Cannot dispose store while a component call is in progress. " +
                    "Dispose the ComponentCallResults first.");
            }
        }

        ReleaseUnmanagedResources();
        Disposed = true;
        GC.SuppressFinalize(this);
    }

    ~Store()
    {
        ReleaseUnmanagedResources();
    }
}