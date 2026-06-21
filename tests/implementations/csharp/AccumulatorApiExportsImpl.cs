namespace TestWorld.wit.Exports.tests.component.v0_1_0;

// Component-exported resource implementation (the host drives it).
public class AccumulatorApiExportsImpl : IAccumulatorApiExports
{
    // Free function in the exported interface.
    public static string Describe() => "accumulator-api";

    public class Accumulator : IAccumulatorApiExports.Accumulator, IAccumulatorApiExports.IAccumulator
    {
        private int _total;

        public Accumulator(int start) => _total = start;

        public int Add(int n)
        {
            _total += n;
            return _total;
        }

        // Static method on the exported resource.
        public static int Combine(int a, int b) => a + b;
    }
}
