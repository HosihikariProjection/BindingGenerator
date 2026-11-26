using System.Globalization;
using System.Numerics;
using Hosihikari.Binding.Ir;

namespace Hosihikari.Binding.Generator;

// A static constexpr field carries its value; the projection is a C# constant and needs
// no carrier symbol. Only plain integer spellings qualify: enum-typed constants keep the
// carrier getter, because the C# enum name is resolved elsewhere.
internal static class ConstantFields
{
    public static bool TryCSharpConstant(MemberRecord member, out string type, out string literal)
    {
        type = "";
        literal = "";
        if (
            member.Value is null
            || member.Type is null
            || !BigInteger.TryParse(
                member.Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            return false;
        }

        switch (Unqualified(member.Type.Canonical))
        {
            case "bool":
                type = "bool";
                literal = value.IsZero ? "false" : "true";
                return true;
            case "char":
            case "signed char":
            case "int8_t":
                type = "sbyte";
                break;
            case "unsigned char":
            case "uint8_t":
            case "char8_t":
                type = "byte";
                break;
            case "short":
            case "int16_t":
                type = "short";
                break;
            case "unsigned short":
            case "uint16_t":
            case "char16_t":
            case "wchar_t":
                type = "ushort";
                break;
            case "int":
            case "int32_t":
                type = "int";
                break;
            case "unsigned int":
            case "uint32_t":
            case "char32_t":
                type = "uint";
                break;
            case "long":
            case "long long":
            case "int64_t":
                type = "long";
                break;
            case "unsigned long":
            case "unsigned long long":
            case "uint64_t":
                type = "ulong";
                break;
            default:
                return false;
        }

        if (!Fits(type, value))
        {
            return false;
        }

        literal = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    // Field types keep their `const` qualifier in the canonical spelling.
    private static string Unqualified(string canonical)
    {
        var text = canonical.Trim();
        while (true)
        {
            if (text.StartsWith("const ", StringComparison.Ordinal))
            {
                text = text["const ".Length..].TrimStart();
            }
            else if (text.StartsWith("volatile ", StringComparison.Ordinal))
            {
                text = text["volatile ".Length..].TrimStart();
            }
            else
            {
                return text;
            }
        }
    }

    public static bool Fits(string type, BigInteger value) =>
        type switch
        {
            "sbyte" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            "byte" => value >= byte.MinValue && value <= byte.MaxValue,
            "short" => value >= short.MinValue && value <= short.MaxValue,
            "ushort" => value >= ushort.MinValue && value <= ushort.MaxValue,
            "int" => value >= int.MinValue && value <= int.MaxValue,
            "uint" => value >= uint.MinValue && value <= uint.MaxValue,
            "long" => value >= long.MinValue && value <= long.MaxValue,
            "ulong" => value >= ulong.MinValue && value <= ulong.MaxValue,
            _ => false,
        };
}
