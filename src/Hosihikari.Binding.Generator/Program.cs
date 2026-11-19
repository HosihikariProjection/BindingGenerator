using Hosihikari.Binding.Generator;
using Hosihikari.Binding.Ir;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return args.Length == 0 ? 2 : 0;
}

switch (args[0])
{
    case "version":
        Console.WriteLine($"Hosihikari.Binding.Generator 0.3.0 (IR {IrSchema.Version})");
        return 0;

    case "validate-ir":
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: validate-ir <ir-manifest.json> <shard-directory>");
            return 2;
        }

        var manifest = ReadManifest(args[1]);
        var records = ReadRecords(args[2]);
        var result = IrValidator.Validate(manifest, records);
        if (!result.IsValid)
        {
            PrintErrors(result);
            return 1;
        }

        Console.WriteLine($"IR valid: {records.Count} records");
        return 0;
    }

    case "emit":
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: emit <ir-manifest.json> <shard-directory> <output-directory> --abi-header <path> [--native-root <path>] [--link-carrier] [--ll-allocator] [--virtual-dispatch] [--symbol-dispatch] [--target-module <name>] [--allow-ir-errors] [--resolve-by-name]");
            return 2;
        }

        string? nativeRoot = null;
        string? abiHeader = null;
        var linkCarrier = false;
        var llAllocator = false;
        var virtualDispatch = false;
        var symbolDispatch = false;
        var allowIrErrors = false;
        var resolveByName = false;
        var targetModule = "bedrock_server";
        for (var index = 4; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--native-root" when index + 1 < args.Length:
                    nativeRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--abi-header" when index + 1 < args.Length:
                    abiHeader = Path.GetFullPath(args[++index]);
                    break;
                case "--link-carrier":
                    linkCarrier = true;
                    break;
                case "--ll-allocator":
                    llAllocator = true;
                    break;
                case "--virtual-dispatch":
                    virtualDispatch = true;
                    break;
                case "--symbol-dispatch":
                    symbolDispatch = true;
                    break;
                // An experimental surface may parse most of its headers while a few
                // declarations fail; the emitted surface is then partial but usable.
                case "--allow-ir-errors":
                    allowIrErrors = true;
                    break;
                // Resolve each member through the carrier's name lookup instead of an
                // address recorded in the IR.
                case "--resolve-by-name":
                    resolveByName = true;
                    break;
                case "--target-module" when index + 1 < args.Length:
                    targetModule = args[++index];
                    break;
                default:
                    Console.Error.WriteLine($"unknown option: {args[index]}");
                    return 2;
            }
        }

        if (abiHeader is null || !File.Exists(abiHeader))
        {
            Console.Error.WriteLine("--abi-header must point to an existing hosihikari_abi.h.");
            return 2;
        }

        if (linkCarrier && nativeRoot is null)
        {
            Console.Error.WriteLine("--link-carrier requires --native-root.");
            return 2;
        }

        if (llAllocator && !linkCarrier)
        {
            Console.Error.WriteLine("--ll-allocator requires --link-carrier.");
            return 2;
        }

        if (virtualDispatch && nativeRoot is null)
        {
            Console.Error.WriteLine("--virtual-dispatch requires --native-root.");
            return 2;
        }

        if (symbolDispatch && (linkCarrier || llAllocator || virtualDispatch))
        {
            Console.Error.WriteLine("--symbol-dispatch cannot be combined with source-based emit modes.");
            return 2;
        }

        var manifest = ReadManifest(args[1]);
        if (allowIrErrors)
        {
            Console.Error.WriteLine(
                $"warning: {manifest.Diagnostics.Errors} parse error(s) ignored by --allow-ir-errors");
            manifest.Diagnostics.Errors = 0;
            manifest.Diagnostics.ShardsWithErrors = [];
        }

        var records = ReadRecords(args[2]);
        var validation = IrValidator.Validate(manifest, records);
        if (!validation.IsValid)
        {
            PrintErrors(validation);
            return 1;
        }

        var manifestHash = "sha256:" + IrHash.CanonicalJsonSha256(File.ReadAllText(args[1]));
        var output = Path.GetFullPath(args[3]);
        if (symbolDispatch)
        {
            records = AddRecordSupport(records, manifest);
            records = AddStaticFields(records, manifest);
        }

        var plan = AbiPlan.Build(records, nativeRoot, linkCarrier, llAllocator, virtualDispatch, symbolDispatch);
        foreach (var subdirectory in new[] { "interop", "shim", "coverage" })
        {
            DeleteDirectory(Path.Combine(output, subdirectory));
        }

        Directory.CreateDirectory(output);
        InteropEmitter.Emit(records, output, plan);
        ShimEmitter.Emit(manifest, records, output, manifestHash, plan, abiHeader, targetModule, resolveByName);
        CoverageEmitter.Emit(manifest, records, output, manifestHash, plan);
        Console.WriteLine(
            $"emitted {records.Count} records to {output} ({plan.Functions.Count} BindingShim functions)");
        return 0;
    }

    case "validate-coverage":
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: validate-coverage <coverage-summary.json> <coverage-manifest.jsonl>");
            return 2;
        }

        using var summaryStream = File.OpenRead(args[1]);
        var summary = IrJson.ReadCoverageSummary(summaryStream);
        using var entriesStream = File.OpenRead(args[2]);
        var entries = IrJson.ReadCoverageEntries(entriesStream);
        var result = IrValidator.ValidateCoverage(summary, entries);
        if (!result.IsValid)
        {
            PrintErrors(result);
            return 1;
        }

        Console.WriteLine($"coverage valid: {entries.Count} entries");
        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown command: {args[0]}");
        PrintHelp();
        return 2;
}

static IrManifest ReadManifest(string path)
{
    using var stream = File.OpenRead(path);
    return IrJson.ReadManifest(stream);
}

static List<IrRecord> ReadRecords(string directory)
{
    var records = new List<IrRecord>();
    foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
                 .OrderBy(path => path, StringComparer.Ordinal))
    {
        using var stream = File.OpenRead(file);
        records.AddRange(IrJson.ReadRecords(stream));
    }

    return records;
}

static void DeleteDirectory(string path)
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return;
        }
        catch (IOException) when (attempt < 2)
        {
            Thread.Sleep(100);
        }
        catch (UnauthorizedAccessException) when (attempt < 2)
        {
            Thread.Sleep(100);
        }
    }
}

static void PrintErrors(IrValidationResult result)
{
    Console.Error.WriteLine($"{result.Errors.Count} error(s):");
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine(" - " + error);
    }
}

static List<IrRecord> AddRecordSupport(List<IrRecord> records, IrManifest manifest)
{
    if (manifest.RecordSupport.Count == 0)
    {
        return records;
    }

    var declarations = records.OfType<DeclarationRecord>()
        .GroupBy(declaration => declaration.QualifiedName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var additions = new List<MemberRecord>();
    foreach (var (name, byConfiguration) in manifest.RecordSupport)
    {
        if (!declarations.TryGetValue(name, out var declaration))
        {
            continue;
        }

        // A header-declared destructor or copy constructor usually has no symbol of its own.
        // Attach the resolved support symbol to that member instead of emitting a second copy.
        // Only public members qualify: a private destructor keeps the synthetic public one.
        var existing = records.OfType<MemberRecord>()
            .Where(member => member.DeclarationId == declaration.Id)
            .ToList();

        foreach (var (configuration, support) in byConfiguration)
        {
            if (support.Destructor is { } destructor)
            {
                var target = existing.FirstOrDefault(member =>
                    member.Kind == "destructor"
                    && member.Access == "public"
                    && member.Configuration == configuration
                    && !HasSupportSymbol(member));
                if (target is not null)
                {
                    ApplySupport(target, destructor);
                }
                else
                {
                    additions.Add(SupportMember(declaration, name, configuration, destructor, copy: false));
                }
            }

            if (support.CopyConstructor is { } copy)
            {
                var target = existing.FirstOrDefault(member =>
                    member.Kind == "constructor"
                    && member.Access == "public"
                    && member.Configuration == configuration
                    && !HasSupportSymbol(member)
                    && IsCopyConstructorOf(member, name));
                if (target is not null)
                {
                    ApplySupport(target, copy);
                }
                else
                {
                    additions.Add(SupportMember(declaration, name, configuration, copy, copy: true));
                }
            }
        }
    }

    return additions.Count == 0 ? records : [.. records, .. additions];

    bool HasSupportSymbol(MemberRecord member) =>
        !string.IsNullOrWhiteSpace(member.Symbol) || member.Rva is not null || member.Address is not null;

    bool IsCopyConstructorOf(MemberRecord member, string name)
    {
        var parameters = member.Signature?.Parameters;
        if (parameters is not { Count: 1 })
        {
            return false;
        }

        var type = parameters[0].Type.Trim();
        while (type.EndsWith("&", StringComparison.Ordinal))
        {
            type = type[..^1].TrimEnd();
        }

        return type.Replace("const ", "", StringComparison.Ordinal)
            .Replace(" const", "", StringComparison.Ordinal)
            .Trim() == name;
    }

    void ApplySupport(MemberRecord member, IrSupportSymbol support)
    {
        member.Symbol = support.Symbol;
        member.Rva = support.Rva;
        member.Address = support.Address;
    }

    MemberRecord SupportMember(
        DeclarationRecord declaration,
        string name,
        string configuration,
        IrSupportSymbol support,
        bool copy) =>
        new()
        {
            Id = declaration.Id + (copy ? "@support:copyConstructor" : "@support:destructor"),
            DeclarationId = declaration.Id,
            Configuration = configuration,
            Kind = copy ? "constructor" : "destructor",
            Name = copy ? name : "~" + name,
            Access = "public",
            Symbol = support.Symbol,
            Rva = support.Rva,
            Address = support.Address,
            Signature = copy
                ? new SignatureInfo
                {
                    Canonical = name + "::" + name,
                    ReturnType = "void",
                    Parameters = [new ParameterInfo { Type = "const " + name + " &" }],
                }
                : new SignatureInfo
                {
                    Canonical = name + "::~" + name,
                    ReturnType = "void",
                },
        };
}

static List<IrRecord> AddStaticFields(List<IrRecord> records, IrManifest manifest)
{
    if (manifest.StaticFields.Count == 0)
    {
        return records;
    }

    var declarations = records.OfType<DeclarationRecord>()
        .GroupBy(declaration => declaration.QualifiedName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    foreach (var (key, byConfiguration) in manifest.StaticFields)
    {
        var separator = key.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
        {
            continue;
        }

        var className = key[..separator];
        var fieldName = key[(separator + 2)..];
        if (!declarations.TryGetValue(className, out var declaration))
        {
            continue;
        }

        foreach (var (configuration, symbol) in byConfiguration)
        {
            var field = records.OfType<MemberRecord>().FirstOrDefault(member =>
                string.Equals(member.DeclarationId, declaration.Id, StringComparison.Ordinal)
                && string.Equals(member.Kind, "static_field", StringComparison.Ordinal)
                && string.Equals(member.Name, fieldName, StringComparison.Ordinal)
                && string.Equals(member.Configuration, configuration, StringComparison.Ordinal));
            if (field is null)
            {
                continue;
            }

            field.Symbol = symbol.Symbol;
            field.Rva = symbol.Rva;
            field.Address = symbol.Address;
        }
    }

    return records;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Hosihikari.Binding.Generator

        Commands:
          version
          validate-ir <ir-manifest.json> <shard-directory>
          validate-coverage <coverage-summary.json> <coverage-manifest.jsonl>
          emit <ir-manifest.json> <shard-directory> <output-directory> --abi-header <path>
        """
    );
}
