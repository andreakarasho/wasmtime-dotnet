using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wasmtime.Interop;

namespace Wasmtime;

internal unsafe static class ComponentResourceDestructor
{
    public static readonly delegate* unmanaged[Cdecl]<void*, wasmtime_context*, uint, wasmtime_error*> DestructorPtr = &Destructor;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static wasmtime_error* Destructor(void* data, wasmtime_context* context, uint rep)
    {
        if (data == null)
        {
            return null;
        }

        var handle = GCHandle.FromIntPtr((nint)data);
        if (handle.Target is not Action<uint> action)
        {
            return null;
        }

        try
        {
            action(rep);
            return null;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(message) + 1];

            fixed (char* utf16 = message)
            fixed (byte* utf8 = bytes)
            {
                var len = System.Text.Encoding.UTF8.GetBytes(utf16, message.Length, utf8, bytes.Length - 1);
                bytes[len] = 0;
                return wasmtime_error_new(utf8);
            }
        }
    }

    public static nint Register(Action<uint> action)
    {
        return GCHandle.ToIntPtr(GCHandle.Alloc(action));
    }

    public static void Unregister(nint data)
    {
        if (data == 0)
        {
            return;
        }

        GCHandle.FromIntPtr(data).Free();
    }
}
