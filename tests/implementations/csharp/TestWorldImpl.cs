using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TestWorld.wit.Imports.tests.component.v0_1_0;
using static TestWorld.wit.Imports.tests.component.v0_1_0.ITypesImports;

namespace TestWorld;

public sealed class TestWorldExportsImpl : ITestWorldExports
{
    public static Dictionary<int, Entity> Entities { get; } = new();

    public static string CombineString(string s1, string s2)
    {
        return s1 + s2;
    }

    public static void AcceptString(string s)
    {
        // No-op
    }

    public static string ReturnString(uint length)
    {
        return new string('a', (int)length);
    }

    public static byte AddU8(byte x, byte y) => (byte)(x + y);
    public static sbyte AddS8(sbyte x, sbyte y) => (sbyte)(x + y);

    public static ushort AddU16(ushort x, ushort y) => (ushort)(x + y);
    public static short AddS16(short x, short y) => (short)(x + y);

    public static uint AddU32(uint x, uint y) => x + y;
    public static int AddS32(int x, int y) => x + y;

    public static ulong AddU64(ulong x, ulong y) => x + y;
    public static long AddS64(long x, long y) => x + y;

    public static double AddF64(double x, double y) => x + y;

    public static float AddF32(float x, float y) => x + y;

    public static Point AddPoint(Point p1, Point p2) => new(p1.x + p2.x, p1.y + p2.y);

    // result<s32, string>: wit-bindgen maps the ok arm to the return value and the err arm to
    // a thrown WitException whose Value is the error payload.
    public static int SafeDivide(int a, int b)
    {
        if (b == 0)
        {
            throw new WitException("division by zero", 0);
        }

        return a / b;
    }

    public static void RegisterEntity(Entity e) => Entities[e.id] = e;

    public static void RegisterEntities(List<Entity> e)
    {
        foreach (var entity in e)
        {
            Entities[entity.id] = entity;
        }
    }

    public static Entity GetEntity(int id) => Entities.TryGetValue(id, out var entity) ? entity : new Entity(-1, "", new Point(0, 0));

    public static List<Entity> GetEntities()
    {
        return Entities.Values.ToList();
    }

    public static string Uppercase(string input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        return input.ToUpperInvariant();
    }

    public static int[] MultiplyList(int[] list, int factor)
    {
        var results = new int[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            results[i] = list[i] * factor;
        }
        return results;
    }

    public static Status ReturnStatus(Status status)
    {
        return status;
    }

    public static List<Status> ReturnStatusList(List<Status> statuses)
    {
        return statuses;
    }

    public static Permission ReturnPermission(Permission permission)
    {
        return permission;
    }

    public static List<Permission> ReturnPermissionList(List<Permission> permissions)
    {
        return permissions;
    }

    private static bool _flag;

    public static void SetFlag(bool flag)
    {
        _flag = flag;
    }

    public static bool GetFlag()
    {
        return _flag;
    }

    public static string GetHostEntityDescription()
    {
        var entity = ITestWorldImports.GetHostEntity();

        return $"Entity {entity.id}: {entity.name}";
    }

    public static uint SumNestedList(List<uint[]> nested)
    {
        uint sum = 0;
        foreach (var list in nested)
        {
            foreach (var value in list)
            {
                sum += value;
            }
        }
        return sum;
    }

    public static void HostCallback()
    {
        ITestWorldImports.Callback();
    }

    public static string HostCombineString(string s1, string s2)
    {
        return ITestWorldImports.CallbackCombineString(s1, s2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void Inner(nint p, int p1)
    {
        AcceptString(Encoding.UTF8.GetString((byte*)p, p1));
    }
}

