using System.Text;
using Hosihikari.Binding.Ir;

namespace Hosihikari.Binding.Generator;

internal static class CoverageEmitter
{
    public static void Emit(
        IrManifest manifest,
        IReadOnlyList<IrRecord> records,
        string outputDirectory,
        string manifestHash,
        AbiPlan plan
    )
    {
        var declarations = records
            .OfType<DeclarationRecord>()
            .OrderBy(declaration => declaration.Id, StringComparer.Ordinal)
            .ToList();
        var members = records
            .OfType<MemberRecord>()
            .GroupBy(member => (member.DeclarationId, member.Configuration))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(member => member.Id, StringComparer.Ordinal).ToList()
            );

        var configurations = manifest
            .Configurations.OrderBy(configuration => configuration.Id, StringComparer.Ordinal)
            .ToList();

        var entries = new List<CoverageEntry>();
        foreach (var declaration in declarations)
        {
            var isTemplate = declaration.Kind == "template_declaration";
            var declarationHasShim = plan.FunctionsByDeclaration.ContainsKey(declaration.Id);
            foreach (var configuration in configurations)
            {
                entries.Add(
                    new CoverageEntry
                    {
                        Id = declaration.Id,
                        Parent = null,
                        Surface = declaration.Surface,
                        Configuration = configuration.Id,
                        Kind = declaration.Kind,
                        QualifiedName = declaration.QualifiedName,
                        Status = isTemplate ? CoverageStatus.Excluded : CoverageStatus.Bound,
                        Emitters =
                            isTemplate ? []
                            : declarationHasShim ? ["interop", "shim"]
                            : ["interop"],
                        Reason = isTemplate
                            ? CoverageReason.TemplateDeclaration
                            : (CoverageReason?)null,
                        Source = declaration.Source,
                    }
                );

                if (
                    !members.TryGetValue(
                        (declaration.Id, configuration.Id),
                        out var declarationMembers
                    )
                )
                {
                    continue;
                }

                foreach (var member in declarationMembers)
                {
                    var isPrivate = member.Access == "private";
                    // Synthetic support members feed by-value copy/destroy planning; they are
                    // not part of the public surface.
                    var isSynthetic = member.Id.Contains("@support:", StringComparison.Ordinal);
                    var excluded = isTemplate || isPrivate || isSynthetic;
                    var memberHasShim =
                        !excluded
                        && (
                            plan.HasMemberFunction(member.Id)
                            || (
                                member.Kind == "destructor"
                                && plan.DestroyByDeclaration.ContainsKey(declaration.Id)
                            )
                        );
                    CoverageReason? reason =
                        isTemplate ? CoverageReason.TemplateDeclaration
                        : isPrivate ? CoverageReason.Private
                        : isSynthetic ? CoverageReason.NotPublic
                        : null;
                    entries.Add(
                        new CoverageEntry
                        {
                            Id = member.Id,
                            Parent = declaration.Id,
                            Surface = declaration.Surface,
                            Configuration = configuration.Id,
                            Kind = member.Kind,
                            QualifiedName = declaration.QualifiedName + "::" + member.Name,
                            Status = excluded ? CoverageStatus.Excluded : CoverageStatus.Bound,
                            Emitters =
                                excluded ? []
                                : memberHasShim ? ["interop", "shim"]
                                : ["interop"],
                            Reason = reason,
                            Source = declaration.Source,
                        }
                    );
                }
            }
        }

        var coverageDirectory = Path.Combine(outputDirectory, "coverage");
        Directory.CreateDirectory(coverageDirectory);
        var coveragePath = Path.Combine(coverageDirectory, "manifest.jsonl");
        using (var stream = File.Create(coveragePath))
        {
            IrJson.WriteCoverageEntries(stream, entries);
        }

        var manifestSha256 = "sha256:" + IrHash.Sha256Hex(File.ReadAllBytes(coveragePath));
        var summary = new CoverageSummary
        {
            IrVersion = manifest.IrVersion,
            IrManifestSha256 = manifestHash,
            Configurations = configurations.Select(configuration => configuration.Id).ToList(),
            Declarations = Count(entries.Where(entry => entry.Parent is null)),
            Members = Count(entries.Where(entry => entry.Parent is not null)),
            BySurface = entries
                .GroupBy(entry => entry.Surface, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Count(group), StringComparer.Ordinal),
            ManifestSha256 = manifestSha256,
        };

        using (var stream = File.Create(Path.Combine(coverageDirectory, "summary.json")))
        {
            IrJson.WriteCoverageSummary(stream, summary);
        }

        var validation = IrValidator.ValidateCoverage(summary, entries);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Emitted coverage failed validation: " + string.Join(" | ", validation.Errors)
            );
        }
    }

    private static CoverageCounts Count(IEnumerable<CoverageEntry> entries)
    {
        var list = entries.ToList();
        return new CoverageCounts
        {
            Total = list.Count,
            Bound = list.Count(entry => entry.Status == CoverageStatus.Bound),
            Skipped = list.Count(entry => entry.Status == CoverageStatus.Skipped),
            Excluded = list.Count(entry => entry.Status == CoverageStatus.Excluded),
            Unknown = list.Count(entry => entry.Status == CoverageStatus.Unknown),
        };
    }
}
