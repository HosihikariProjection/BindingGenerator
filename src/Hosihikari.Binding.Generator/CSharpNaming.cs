using Hosihikari.Binding.Ir;

namespace Hosihikari.Binding.Generator;

internal readonly record struct DeclarationName(
    string NamespaceName,
    string NamespacePath,
    string ClassName
);

internal static class CSharpNaming
{
    public static Dictionary<string, DeclarationName> BuildDeclarationNames(
        IReadOnlyList<DeclarationRecord> declarations,
        IReadOnlyCollection<string>? extraClassNames = null
    )
    {
        var classNames = declarations
            .Select(declaration => declaration.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        if (extraClassNames is not null)
        {
            classNames.UnionWith(extraClassNames);
        }

        var result = new Dictionary<string, DeclarationName>(StringComparer.Ordinal);
        var candidates = declarations
            .Select(declaration =>
            {
                var (namespaceName, namespacePath) = BuildNamespace(declaration, classNames);
                return (
                    Declaration: declaration,
                    NamespaceName: namespaceName,
                    NamespacePath: namespacePath,
                    Class: LimitName(
                        TypeNames.Sanitize(TypeNames.NativeName(declaration.QualifiedName)),
                        declaration.Id,
                        96
                    )
                );
            })
            .ToList();

        foreach (
            var group in candidates.GroupBy(candidate => (candidate.NamespaceName, candidate.Class))
        )
        {
            var duplicate = group.Count() > 1;
            foreach (var candidate in group)
            {
                var className = duplicate
                    ? candidate.Class + "_" + ShortHash(candidate.Declaration.Id)
                    : candidate.Class;
                result[candidate.Declaration.Id] = new DeclarationName(
                    candidate.NamespaceName,
                    candidate.NamespacePath,
                    className
                );
            }
        }

        return result;
    }

    public static string ShortHash(string value) => IrHash.Sha256Hex(value).Substring(0, 8);

    private static string LimitName(string name, string key, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        return name[..maxLength] + "_" + ShortHash(key);
    }

    private static (string NamespaceName, string NamespacePath) BuildNamespace(
        DeclarationRecord declaration,
        HashSet<string> classNames
    )
    {
        var parts = TypeNames.SplitQualifiedName(declaration.QualifiedName);
        var segments = new List<string>();
        var nativePrefix = "";
        var parentCount = Math.Max(0, parts.Count - 1);
        for (var index = 0; index < parentCount; index++)
        {
            var part = parts[index];
            nativePrefix = nativePrefix.Length == 0 ? part : nativePrefix + "::" + part;
            var segment = TypeNames.Sanitize(part);
            if (classNames.Contains(nativePrefix))
            {
                segment = LimitName(segment + "_Nested", nativePrefix + ":nested", 72);
            }
            else
            {
                segment = LimitName(segment, nativePrefix, 64);
            }

            segments.Add(segment);
        }

        var baseName =
            $"Hosihikari.Binding.{TypeNames.SurfaceNamespace(declaration.Surface)}.Interop";
        var namespaceName =
            segments.Count == 0 ? baseName : baseName + "." + string.Join(".", segments);
        return (namespaceName, string.Join(".", segments));
    }
}
