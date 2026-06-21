namespace Wasmtime.Tests;

internal class TestImportsImpl : Wit.Tests.Component.TestImports
{
    public bool CallbackWasCalled { get; private set; }
    public bool CallbackCombineStringWasCalled { get; private set; }

    public Wit.Tests.Component.Types.Entity Entity { get; set; } = new Wit.Tests.Component.Types.Entity
    {
        Id = 1,
        Name = "Default Entity"
    };

    public override void Callback()
    {
        CallbackWasCalled = true;
    }

    public override string CallbackCombineString(string s1, string s2)
    {
        CallbackCombineStringWasCalled = true;
        return s1 + s2;
    }

    public override Wit.Tests.Component.Types.Entity GetHostEntity()
    {
        return Entity;
    }

    // Imported resource: host provides the `counter` implementation the component constructs/uses.
    public override ICounter NewCounter(int initial) => new CounterImpl(initial);

    // Static method on the imported resource (no instance / no self).
    public override int CounterMerge(int a, int b) => a + b;

    internal static int CounterDisposeCount;

    // A struct, to exercise that the generated resource type is an interface the host may
    // implement on either a struct or a class. (It boxes once when stored in the handle table.)
    private struct CounterImpl : ICounter
    {
        private int _value;

        public CounterImpl(int initial) => _value = initial;

        public int Increment(int by)
        {
            _value += by;
            return _value;
        }

        public int Value() => _value;

        public void Dispose()
        {
            System.Threading.Interlocked.Increment(ref CounterDisposeCount);
        }
    }
}
