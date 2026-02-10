using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Wasmtime.Interop;

namespace Wasmtime;

public interface IComponentImports
{
    void Register(Linker linker);
}

public sealed unsafe class LinkerInstance : IDisposable
{
    internal wasmtime_component_linker_instance_t* Handle;
    private readonly Linker _linker;

    internal LinkerInstance(wasmtime_component_linker_instance_t* handle, Linker linker)
    {
        Handle = handle;
        _linker = linker;
    }

    public void DefineFunction(string name, ComponentFunctionDelegate function, object? state = null)
    {
        var data = ComponentExport.RegisterFunction(function, state);
        var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
            ? stackalloc byte[256]
            : new byte[Encoding.UTF8.GetByteCount(name)];

        fixed (char* utf16 = name)
        fixed (byte* utf8 = bytes)
        {
            var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

            var error = wasmtime_component_linker_instance_add_func(
                Handle,
                utf8,
                (nuint)length,
                (nint)ComponentExport.CallerPtr,
                (void*)data,
                IntPtr.Zero
            );

            WasmtimeException.ThrowIfError(error);
        }
    }

    public LinkerInstance DefineInstance(string name)
    {
        var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
            ? stackalloc byte[256]
            : new byte[Encoding.UTF8.GetByteCount(name)];

        fixed (char* utf16 = name)
        fixed (byte* utf8 = bytes)
        {
            var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

            wasmtime_component_linker_instance_t* childHandle;
            var error = wasmtime_component_linker_instance_add_instance(
                Handle,
                utf8,
                (nuint)length,
                &childHandle
            );

            WasmtimeException.ThrowIfError(error);
            return new LinkerInstance(childHandle, _linker);
        }
    }

    /// <summary>
    /// Defines a resource type within this linker instance.
    /// The resource is identified by a unique type ID and has an optional destructor callback.
    /// </summary>
    public void DefineResource(string name, uint typeId)
        => DefineResource(name, typeId, null);

    /// <summary>
    /// Defines a resource type within this linker instance with a destructor callback.
    /// </summary>
    public void DefineResource(string name, uint typeId, Action<uint>? destructor)
    {
        var resourceType = wasmtime_component_resource_type_new_host(typeId);
        try
        {
            nint destructorData = 0;
            if (destructor != null)
            {
                destructorData = _linker.RegisterResourceDestructor(destructor);
            }

            var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
                ? stackalloc byte[256]
                : new byte[Encoding.UTF8.GetByteCount(name)];

            fixed (char* utf16 = name)
            fixed (byte* utf8 = bytes)
            {
                var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

                var error = wasmtime_component_linker_instance_add_resource(
                    Handle,
                    utf8,
                    (nuint)length,
                    resourceType,
                    (IntPtr)ComponentResourceDestructor.DestructorPtr,
                    (void*)destructorData,
                    IntPtr.Zero
                );

                WasmtimeException.ThrowIfError(error);
            }
        }
        finally
        {
            wasmtime_component_resource_type_delete(resourceType);
        }
    }

    public void Dispose()
    {
        if (Handle != null)
        {
            wasmtime_component_linker_instance_delete(Handle);
            Handle = null;
        }
    }
}

public sealed unsafe class Linker : IDisposable
{
    internal wasmtime_linker* Handle;
    internal wasmtime_component_linker_t* ComponentHandle;
    private readonly List<nint> _resourceDestructorHandles = new();

    public Linker(Engine engine)
    {
        Handle = wasmtime_linker_new(engine.Handle);
        ComponentHandle = wasmtime_component_linker_new(engine.Handle);
    }

    internal nint RegisterResourceDestructor(Action<uint> destructor)
    {
        var handle = ComponentResourceDestructor.Register(destructor);
        _resourceDestructorHandles.Add(handle);
        return handle;
    }

    internal bool IsWasiP2Added { get; private set; }

    public void AddWasiP2()
    {
        wasmtime_component_linker_add_wasip2(ComponentHandle);
        IsWasiP2Added = true;
    }

    public void Define(IComponentImports imports)
    {
        imports.Register(this);
    }

    public void DefineFunction(string name, ComponentFunctionDelegate function, object? state = null)
    {
        var data = ComponentExport.RegisterFunction(function, state);
        var root = wasmtime_component_linker_root(ComponentHandle);
        try
        {
            var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
                ? stackalloc byte[256]
                : new byte[Encoding.UTF8.GetByteCount(name)];

            fixed (char* utf16 = name)
            fixed (byte* utf8 = bytes)
            {
                var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

                var error = wasmtime_component_linker_instance_add_func(
                    root,
                    utf8,
                    (nuint)length,
                    (nint)ComponentExport.CallerPtr,
                    (void*)data,
                    IntPtr.Zero
                );

                WasmtimeException.ThrowIfError(error);
            }
        }
        finally
        {
            wasmtime_component_linker_instance_delete(root);
        }
    }

    /// <summary>
    /// Defines a resource type at the root level of the linker.
    /// Required when root-level functions reference resources from imported interfaces.
    /// </summary>
    public void DefineResource(string name, uint typeId)
        => DefineResource(name, typeId, null);

    /// <summary>
    /// Defines a resource type at the root level of the linker with a destructor callback.
    /// Required when root-level functions reference resources from imported interfaces.
    /// </summary>
    public void DefineResource(string name, uint typeId, Action<uint>? destructor)
    {
        var root = wasmtime_component_linker_root(ComponentHandle);
        try
        {
            var resourceType = wasmtime_component_resource_type_new_host(typeId);
            try
            {
                nint destructorData = 0;
                if (destructor != null)
                {
                    destructorData = RegisterResourceDestructor(destructor);
                }

                var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
                    ? stackalloc byte[256]
                    : new byte[Encoding.UTF8.GetByteCount(name)];

                fixed (char* utf16 = name)
                fixed (byte* utf8 = bytes)
                {
                    var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

                    var error = wasmtime_component_linker_instance_add_resource(
                        root,
                        utf8,
                        (nuint)length,
                        resourceType,
                        (IntPtr)ComponentResourceDestructor.DestructorPtr,
                        (void*)destructorData,
                        IntPtr.Zero
                    );

                    WasmtimeException.ThrowIfError(error);
                }
            }
            finally
            {
                wasmtime_component_resource_type_delete(resourceType);
            }
        }
        finally
        {
            wasmtime_component_linker_instance_delete(root);
        }
    }

    public LinkerInstance DefineInstance(string name)
    {
        var root = wasmtime_component_linker_root(ComponentHandle);
        try
        {
            var bytes = Encoding.UTF8.GetMaxByteCount(name.Length) <= 256
                ? stackalloc byte[256]
                : new byte[Encoding.UTF8.GetByteCount(name)];

            fixed (char* utf16 = name)
            fixed (byte* utf8 = bytes)
            {
                var length = Encoding.UTF8.GetBytes(utf16, name.Length, utf8, bytes.Length);

                wasmtime_component_linker_instance_t* childHandle;
                var error = wasmtime_component_linker_instance_add_instance(
                    root,
                    utf8,
                    (nuint)length,
                    &childHandle
                );

                WasmtimeException.ThrowIfError(error);
                return new LinkerInstance(childHandle, this);
            }
        }
        finally
        {
            wasmtime_component_linker_instance_delete(root);
        }
    }

    private void ReleaseUnmanagedResources()
    {
        if (Handle != null)
        {
            wasmtime_linker_delete(Handle);
            Handle = null;
        }

        if (ComponentHandle != null)
        {
            wasmtime_component_linker_delete(ComponentHandle);
            ComponentHandle = null;
        }

        if (_resourceDestructorHandles.Count > 0)
        {
            foreach (var handle in _resourceDestructorHandles)
            {
                ComponentResourceDestructor.Unregister(handle);
            }
            _resourceDestructorHandles.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~Linker()
    {
        ReleaseUnmanagedResources();
    }
}
