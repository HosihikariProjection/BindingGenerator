using System.Text;
using Hosihikari.Binding.LeviLamina.Interop;
using Hosihikari.Binding.LeviLamina.Interop.ll.@event;
using Hosihikari.Binding.LeviLamina.Interop.ll.hash_utils;
using LlVersion = Hosihikari.Binding.LeviLamina.Interop.ll.data.Version;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: Smoke <path-to-shim.dll>");
    return 2;
}

NativeMethods.Load(args[0]);

const string text = "hosihikari";
using var id = EventId.Create(text);
if (id.name != text)
{
    throw new InvalidOperationException($"EventId.name was '{id.name}'.");
}

var baseView = new HashedIdBase(id.Handle);
var expected = Fnv1a(text);
if (baseView.hash != expected)
{
    throw new InvalidOperationException(
        $"EventId.hash was 0x{baseView.hash:x16}, expected 0x{expected:x16}."
    );
}

if (!baseView.operator_equal(new HashedIdBase(id.Handle)))
{
    throw new InvalidOperationException(
        "HashedIdBase::operator== returned false for equal hashes."
    );
}

using var combiner = HashCombiner.Create(123);
if (combiner.Handle == 0)
{
    throw new InvalidOperationException("HashCombiner.Create returned a null handle.");
}

if (combiner.hash() != 123 || combiner.operator_unsigned_long_long() != 123)
{
    throw new InvalidOperationException("HashCombiner method calls returned an unexpected seed.");
}

using var version = LlVersion.Create("1.2.3");
if (
    version.to_string() != "1.2.3"
    || version.major != 1
    || version.minor != 2
    || version.patch != 3
)
{
    throw new InvalidOperationException("Version round-trip returned unexpected values.");
}

if (!LlVersion.valid("1.2.3"))
{
    throw new InvalidOperationException("Version::valid rejected a valid version.");
}

using var sameVersion = LlVersion.Create("1.2.3");
if (!version.isIdenticalTo(sameVersion))
{
    throw new InvalidOperationException("Version::isIdenticalTo rejected equal versions.");
}

Console.WriteLine($"name={id.name} hash=0x{baseView.hash:x16} expected=0x{expected:x16}");
Console.WriteLine("BindingShim smoke test passed.");
return 0;

static ulong Fnv1a(string value)
{
    var hash = 0xcbf29ce484222325UL;
    foreach (var b in Encoding.UTF8.GetBytes(value))
    {
        hash ^= b;
        hash *= 0x100000001b3UL;
    }

    return hash;
}
