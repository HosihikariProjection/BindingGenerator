using System.Runtime.InteropServices;
using Hosihikari.Bindings.Bedrock.Interop;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: SymbolDispatchSmoke <fixture-module> <dispatch-shim>");
    return 2;
}

NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeMethods.Load(args[1]);

var block = FixtureBlock.create(2, 3)
    ?? throw new InvalidOperationException("FixtureBlock.create returned null.");

if (block.add(4) != 9)
{
    throw new InvalidOperationException("RVA dispatch returned an unexpected value.");
}

if (block.isReady() != 1)
{
    throw new InvalidOperationException("The first vtable slot returned an unexpected value.");
}

if (block.scale(5) != 25)
{
    throw new InvalidOperationException("The second vtable slot returned an unexpected value.");
}

if (block.label() != "5")
{
    throw new InvalidOperationException("The by-value string return is incorrect.");
}

if (!block.numbers().SequenceEqual([2, 3]))
{
    throw new InvalidOperationException("The by-value int vector return is incorrect.");
}

// FixturePair has a user-provided copy constructor but a trivial destructor, so it is passed
// by hidden reference even though it is only 8 bytes.
using (var pair = block.pair())
{
    if (block.sumPair(pair) != 7)
    {
        throw new InvalidOperationException("The trivially destructible by-value record is incorrect.");
    }
}

if (block.maybeNumber(true) != 2 || block.maybeNumber(false) is not null)
{
    throw new InvalidOperationException("The optional scalar value is incorrect.");
}

if (block.sumOptional(5) != 7)
{
    throw new InvalidOperationException("The optional scalar parameter is incorrect.");
}

using (var value = block.maybeValue(true))
{
    if (value is null || block.sumValueOptional(value) != 7)
    {
        throw new InvalidOperationException("The optional record value is incorrect.");
    }
}

if (block.maybeValue(false) is not null)
{
    throw new InvalidOperationException("The empty optional record value is incorrect.");
}

// std::optional<string> is only present in the PDB-derived Windows IR; the checked-in Linux
// fixture keeps to manglings that do not depend on the cxx11 ABI tag.
if (typeof(FixtureBlock).GetMethod("maybeLabel") is not null)
{
    var maybeLabel = typeof(FixtureBlock).GetMethod("maybeLabel")!;
    if ((string?)maybeLabel.Invoke(block, [true]) != "2" || (string?)maybeLabel.Invoke(block, [false]) is not null)
    {
        throw new InvalidOperationException("The optional string value is incorrect.");
    }

    var labelSize = typeof(FixtureBlock).GetMethod("labelSize")!;
    if ((int)labelSize.Invoke(block, ["abc"])! != 5)
    {
        throw new InvalidOperationException("The optional string parameter is incorrect.");
    }
}

var shared = block.makeShared(42)
    ?? throw new InvalidOperationException("FixtureBlock.makeShared returned null.");
try
{
    if (block.sharedValue(shared) != 44)
    {
        throw new InvalidOperationException("The shared_ptr parameter is incorrect.");
    }
}
finally
{
    shared.Dispose();
}

var weak = block.weakRef()
    ?? throw new InvalidOperationException("FixtureBlock.weakRef returned an expired handle.");
try
{
    if (block.sharedValue(weak) != 44)
    {
        throw new InvalidOperationException("The weak_ptr snapshot is incorrect.");
    }
}
finally
{
    weak.Dispose();
}

// The checked-in Linux fixture predates the reference wrapper methods; the Windows
// PDB-derived IR exercises them through the same generated surface.
void CheckReferenceWrapper(string name, int value, int expected)
{
    var method = typeof(FixtureBlock).GetMethod(name);
    if (method is null)
    {
        return;
    }

    using var handle = (global::Hosihikari.Bindings.Abi.SharedHandle?)method.Invoke(block, [value]);
    if (handle is null || handle.Target == 0 || block.sharedValue(handle) != expected)
    {
        throw new InvalidOperationException($"The {name} result is incorrect.");
    }
}

CheckReferenceWrapper("ownerPtr", 51, 53);
CheckReferenceWrapper("stackRef", 61, 63);
CheckReferenceWrapper("weakRefValue", 71, 73);
CheckReferenceWrapper("notNullOwner", 81, 83);

var heavyMethod = typeof(FixtureBlock).GetMethod("heavy");
if (heavyMethod is not null)
{
    var heavy = heavyMethod.Invoke(block, [1234]);
    try
    {
        var heavySize = typeof(FixtureBlock).GetMethod("heavySize")!;
        if ((int)heavySize.Invoke(block, [heavy])! != 4)
        {
            throw new InvalidOperationException("The non-trivial by-value record return is incorrect.");
        }
    }
    finally
    {
        (heavy as IDisposable)?.Dispose();
    }
}

var objectSpanMethod = typeof(FixtureBlock).GetMethod("objectSpan");
if (objectSpanMethod is not null)
{
    var span = (nint[])objectSpanMethod.Invoke(block, [])!;
    if (span.Length != 2 || span[0] == 0 || span[1] == 0)
    {
        throw new InvalidOperationException("The gsl::span pointer result is incorrect.");
    }
}

var uniqueHeavyMethod = typeof(FixtureBlock).GetMethod("uniqueHeavy");
if (uniqueHeavyMethod is not null)
{
    var heavy = uniqueHeavyMethod.Invoke(block, [4321]);
    try
    {
        var heavySize = typeof(FixtureBlock).GetMethod("heavySize")!;
    }
    finally
    {
        (heavy as IDisposable)?.Dispose();
    }
}

// A raw handle is nullable: the shim reports a handle it could not produce as absent
// rather than as zero.
var objectA = block.makeObject(10)
    ?? throw new InvalidOperationException("The first object handle is null.");
var objectB = block.makeObject(20)
    ?? throw new InvalidOperationException("The second object handle is null.");
if (block.sumObjectValues([objectA, objectB]) != 32)
{
    throw new InvalidOperationException("The pointer vector parameter is incorrect.");
}

block.releaseObject(objectA);
block.releaseObject(objectB);

// The checked-in Linux fixture IR covers the member string/vector returns above. The PDB-derived
// Windows IR additionally contains the parameter and static cases; reflect so both IRs share
// this program.
if (typeof(FixtureBlock).GetMethod("names") is not null)
{
    if (!Call<string[]>(block, "names").SequenceEqual(["2", "3"]))
    {
        throw new InvalidOperationException("The by-value string vector return is incorrect.");
    }

    if (!Call<int[]>(block, "squares", 3).SequenceEqual([0, 1, 4]))
    {
        throw new InvalidOperationException("The static int vector return is incorrect.");
    }

    if (Call<int>(block, "countNames", (object)new[] { "a", "b" }) != 4)
    {
        throw new InvalidOperationException("The string vector parameter is incorrect.");
    }

    if (Call<int>(block, "sumValues", (object)new[] { 1, 2, 3 }) != 9)
    {
        throw new InvalidOperationException("The int vector parameter is incorrect.");
    }
}

FixtureBlock.release(block);
FixtureBlock.release(null);

Console.WriteLine("Symbol dispatch smoke test passed.");
return 0;

static T Call<T>(FixtureBlock target, string name, params object?[] arguments)
{
    var method = typeof(FixtureBlock).GetMethod(name)
        ?? throw new InvalidOperationException($"FixtureBlock.{name} is unavailable.");
    return (T)method.Invoke(method.IsStatic ? null : target, arguments)!;
}
