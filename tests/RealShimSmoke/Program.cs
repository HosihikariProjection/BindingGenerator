using Hosihikari.Binding.LeviLamina.Interop;
using Hosihikari.Binding.LeviLamina.Interop.demo;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: RealShimSmoke <path-to-shim>");
    return 2;
}

NativeMethods.Load(args[0]);

using var counter = Counter.Create(7);
if (counter.value != 7 || counter.get() != 7)
{
    throw new InvalidOperationException("Inline Counter calls returned unexpected values.");
}

using var linked = LinkedCounter.Create(42);
if (linked.value() != 42)
{
    throw new InvalidOperationException("Out-of-line LinkedCounter calls returned an unexpected value.");
}

Console.WriteLine("Real BindingShim smoke test passed.");
return 0;
