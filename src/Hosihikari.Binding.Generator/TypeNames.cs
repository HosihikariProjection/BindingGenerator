using Hosihikari.Binding.Ir;

namespace Hosihikari.Binding.Generator;

internal static class TypeNames
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    };

    public static string SurfaceNamespace(string surface) =>
        surface switch
        {
            "bedrock" => "Bedrock",
            "levilamina" => "LeviLamina",
            "endstone" => "Endstone",
            _ => "Unknown",
        };

    public static string Escape(string name) => Keywords.Contains(name) ? "@" + name : name;

    public static string Sanitize(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0)
        {
            builder.Append('_');
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return Escape(builder.ToString());
    }

    public static IReadOnlyList<string> SplitQualifiedName(string name)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < name.Length - 1; index++)
        {
            var character = name[index];
            if (character == '<')
            {
                depth++;
            }
            else if (character == '>')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && character == ':' && name[index + 1] == ':')
            {
                parts.Add(name[start..index]);
                start = index + 2;
                index++;
            }
        }

        parts.Add(name[start..]);
        return parts;
    }

    public static string NativeNamespace(string qualifiedName)
    {
        var parts = SplitQualifiedName(qualifiedName);
        return parts.Count > 1 ? string.Join(".", parts.Take(parts.Count - 1)) : "";
    }

    public static string NativeName(string qualifiedName)
    {
        var parts = SplitQualifiedName(qualifiedName);
        return parts.Count == 0 ? qualifiedName : parts[^1];
    }

    public static string ToCSharpType(TypeRef? type)
    {
        if (type is null)
        {
            return "nint";
        }

        var canonical = type.Canonical.Trim();
        return canonical switch
        {
            "void" => "void",
            "bool" => "bool",
            "char" or "signed char" or "unsigned char" or "int8_t" or "uint8_t" => "byte",
            "short" or "int16_t" => "short",
            "unsigned short" or "uint16_t" => "ushort",
            "int" or "int32_t" => "int",
            "unsigned int" or "uint32_t" => "uint",
            "long long" or "int64_t" => "long",
            "unsigned long long" or "uint64_t" => "ulong",
            "float" => "float",
            "double" => "double",
            "size_t" => "nuint",
            "long" => "nint",
            "unsigned long" => "nuint",
            _ => type.Kind == "fundamental" ? "nint" : "nint",
        };
    }
}
