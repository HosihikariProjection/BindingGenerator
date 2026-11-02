using Hosihikari.Bindings.Ir;

namespace Hosihikari.Bindings.Generator;

internal enum AbiKind
{
    Void,
    Bool,
    I8,
    U8,
    I16,
    U16,
    I32,
    U32,
    I64,
    U64,
    USize,
    ISize,
    F32,
    F64,
    Opaque,
    StringView,
    Callback,
    ArrayView,
    Vector,
    Shared,
    MapView,
}

internal enum AbiFunctionKind
{
    Create,
    Destroy,
    Get,
    Set,
    Method,
    Cast,
    DirectorCreate,
    FreeFunction,
}

internal enum AbiCastKind
{
    None,
    Upcast,
    Downcast,
}

internal sealed class AbiParameter
{
    public string Name { get; init; } = "";
    public AbiKind Kind { get; init; }
    public string NativeType { get; init; } = "";
    public string? NativePointee { get; init; }
    public string? ByValueDeclarationId { get; init; }
    public bool IsByValueRecord { get; init; }

    /// <summary>
    /// True when a by-value record can travel in integer registers. Records that are only
    /// trivially destructible use the memory convention even when they are small.
    /// </summary>
    public bool ByValueTriviallyCopyable { get; init; }

    /// <summary>
    /// True when the record's destructor is trivial. A non-trivial destructor means the
    /// callee destroys the by-value argument; a byte copy is only a valid stand-in when the
    /// copy constructor is trivial as well.
    /// </summary>
    public bool ByValueTriviallyDestructible { get; init; }

    public bool IsPointer => AbiPlan.IsPointerType(NativeType);
    public bool IsReference { get; init; }
    public bool IsRValueReference { get; init; }
    public bool IsConst { get; init; }

    /// <summary>
    /// True for a non-const std::string lvalue reference. The callee rewrites the text in
    /// place, so the shim hands it a real string and reports the rewritten value back as
    /// the call result.
    /// </summary>
    public bool IsMutableString { get; init; }
    public bool IsOptional { get; init; }
    public string? OptionalInnerType { get; init; }

    /// <summary>True when the optional payload is a trivially copyable record.</summary>
    public bool IsOptionalRecord { get; init; }
    public bool IsOptionalRef { get; init; }
    public string? OptionalRefInnerType { get; init; }
    public bool IsVector { get; init; }

    /// <summary>
    /// Which array-like parameter this is. `vector` builds a std::vector from the element
    /// buffer; `span` and `initializer` pass the buffer straight through as the two-word
    /// object the native parameter expects.
    /// </summary>
    public VectorParameterKind VectorKind { get; init; } = VectorParameterKind.Vector;
    public AbiKind VectorElementKind { get; init; }
    public string? VectorElementType { get; init; }
    public string? VectorElementWrapperType { get; init; }
    public ArrayElementOwnership VectorElementOwnership { get; init; }
    public string? VectorElementNativeType { get; init; }
    public AbiKind VectorPairKeyKind { get; init; }
    public AbiKind VectorPairValueKind { get; init; }
    public string? VectorPairKeyType { get; init; }
    public string? VectorPairValueType { get; init; }
    public bool IsWeakPtr { get; init; }
    public bool IsBitset { get; init; }
    public int BitsetBits { get; init; }
    public bool IsUniquePtrParam { get; init; }
    public bool IsMap { get; init; }
    public AbiKind MapKeyKind { get; init; }
    public AbiKind MapValueKind { get; init; }
    public string MapKeyNativeType { get; init; } = "";
    public string MapValueNativeType { get; init; } = "";
    public string? MapValueWrapperType { get; init; }
    public ArrayElementOwnership MapValueOwnership { get; init; }
    public string? WrapperType { get; init; }
    public AbiCallback? Callback { get; init; }
    public VariantAbiInfo? VariantInfo { get; init; }
    public TimeConversion TimeConversion { get; init; }
    public string? TimeClockType { get; init; }
    public string? TimeDurationType { get; init; }
    public SpecialValueKind SpecialValue { get; init; }
    public string? SpecialValueType { get; init; }
}

internal enum VariantOp
{
    None,
    Create,
    Index,
    Get,
}

internal enum VariantAlternativeKind
{
    String,
    Record,
    Pointer,
    Scalar,
}

internal sealed class VariantAlternative
{
    public int Index { get; init; }
    public string NativeType { get; init; } = "";
    public VariantAlternativeKind Kind { get; init; }
    public AbiKind ScalarKind { get; init; }
    public string? WrapperType { get; init; }
    public string ManagedName { get; init; } = "";
}

internal sealed class VariantAbiInfo
{
    public string NativeType { get; init; } = "";
    public string Surface { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string WrapperType => "global::" + NamespaceName + "." + ClassName;
    public string NamespaceName => $"Hosihikari.Bindings.{TypeNames.SurfaceNamespace(Surface)}.Interop";
    public List<VariantAlternative> Alternatives { get; init; } = [];
    public List<AbiFunction> CreateFunctions { get; } = [];
    public List<AbiFunction> GetFunctions { get; } = [];
    public AbiFunction? IndexFunction { get; set; }
    public AbiFunction? DestroyFunction { get; set; }
    public bool Used { get; set; }
}

internal sealed class AbiCallback
{
    public string NativeType { get; init; } = "";
    public string ReturnNativeType { get; init; } = "";
    public AbiKind ReturnKind { get; init; }
    public List<AbiCallbackArgument> Arguments { get; init; } = [];
}

internal sealed class AbiCallbackArgument
{
    public string NativeType { get; init; } = "";
    public AbiKind Kind { get; init; }
    public bool IsReference { get; init; }
    public bool IsConst { get; init; }
    public bool IsPointer { get; init; }
}

internal enum SharedWrapperKind
{
    None,
    SharedPtr,
    WeakPtr,
    NotNull,
    Nullable,
    NonOwnerPtr,
}

internal enum SpecialValueKind
{
    None,
    TypeId,
    StrongOrdering,
    Path,
}

internal enum TimeConversion
{
    None,
    SystemTimePoint,
    ClockTimePoint,
    Duration,
}

internal enum ArrayElementOwnership
{
    None,
    UniquePtr,
    RecordCopy,
    OptionalRecordCopy,
    VariantCopy,
    PairCopy,
}

internal enum VectorParameterKind
{
    Vector,
    Span,
    InitializerList,
}

internal sealed class AbiFunction
{
    public int Index { get; set; }

    /// <summary>
    /// Stable table field name derived from the function identity. The table order remains
    /// positional, but adding a function no longer renames every call site.
    /// </summary>
    public string TableField => CppName;
    public string CppName { get; set; } = "";
    public AbiFunctionKind Kind { get; init; }
    public AbiCastKind CastKind { get; init; }
    public string? TargetCppName { get; init; }
    public string? TargetDeclarationId { get; init; }
    public int DirectorConstructorParameterCount { get; init; }
    public List<AbiParameter> DirectorCallbackParameters { get; init; } = [];
    public AbiKind DirectorReturnKind { get; init; }
    public string DirectorReturnNativeType { get; init; } = "void";
    public bool DirectorMethodIsConst { get; init; }
    public string Surface { get; init; } = "";
    public string DeclarationId { get; init; } = "";
    public string? MemberId { get; init; }
    public string ClassCppName { get; init; } = "";
    public DeclarationName ClassName { get; init; }
    public string NativeMemberName { get; init; } = "";
    public string CSharpMemberName { get; init; } = "";
    public AbiKind ReturnKind { get; init; }
    public string NativeReturnType { get; init; } = "";
    public string? ReturnWrapperType { get; init; }
    public bool ReturnOwned { get; init; }
    public bool ReturnBorrowed => ReturnKind == AbiKind.Opaque && ReturnWrapperType is not null && !ReturnOwned;
    public bool ReturnUniquePtr { get; init; }
    public bool ReturnOptional { get; init; }
    public bool ReturnArray { get; init; }
    public bool ReturnSpan { get; init; }
    public AbiKind ArrayElementKind { get; init; }
    public string? ArrayElementType { get; init; }
    public SharedWrapperKind ArrayElementSharedWrapper { get; init; }
    public ArrayElementOwnership ArrayElementOwnership { get; init; }
    public string? ArrayElementWrapperType { get; init; }
    public string? ArrayElementNativeType { get; init; }
    public SharedWrapperKind SharedWrapper { get; init; }
    public AbiKind MapKeyKind { get; init; }
    public AbiKind MapValueKind { get; init; }
    public string? MapValueWrapperType { get; init; }
    public ArrayElementOwnership MapValueOwnership { get; init; }
    public string? MapValueNativeType { get; init; }
    public bool ReturnNullable { get; init; }
    public string? ReturnDeclarationId { get; init; }
    public bool ReturnRequiresCopy { get; init; }

    /// <summary>True when a by-value record return can be read from the register result.</summary>
    public bool ReturnTriviallyCopyable { get; init; }

    /// <summary>True when the return is `std::optional` over a trivially copyable record.</summary>
    public bool ReturnOptionalRecord { get; init; }

    /// <summary>Absolute bit offset for symbol-dispatched field accessors.</summary>
    public long? FieldBitOffset { get; init; }

    /// <summary>Set for a bitfield; the accessor reads or writes this many bits.</summary>
    public int? FieldBitWidth { get; init; }

    /// <summary>True when a field getter returns the address of the field storage.</summary>
    public bool FieldAddress { get; init; }

    /// <summary>Bit width when the member is a std::bitset field accessed as an integer.</summary>
    public bool IsBitset { get; init; }
    public int BitsetBits { get; init; }

    public bool CreateViaBorrowedHolder { get; init; }
    public bool DestroyViaBorrowedHolder { get; init; }
    public bool ReturnExpected { get; init; }
    public string? ExpectedErrorType { get; init; }
    public bool NeedsPresenceFlag => ReturnOptional || ReturnExpected;
    public List<AbiParameter> Parameters { get; init; } = [];

    /// <summary>
    /// True when the native call returns void but rewrites a std::string argument in place;
    /// the shim reports that text as the call result instead.
    /// </summary>
    public bool MutableStringOut { get; init; }

    /// <summary>
    /// True when the native return keeps its own value, so the rewritten string argument
    /// travels through an extra trailing <c>HosihikariStringView*</c> out parameter.
    /// </summary>
    public bool MutableStringOutRef { get; init; }
    public bool Static { get; init; }

    /// <summary>
    /// The member is declared with an <c>&amp;&amp;</c> qualifier, so the shim has to call it on
    /// an rvalue even though the wrapper keeps calling the same object.
    /// </summary>
    public bool RvalueQualified { get; init; }
    public VariantAbiInfo? VariantInfo { get; init; }
    public VariantOp VariantOp { get; init; }
    public VariantAlternative? VariantAlternative { get; init; }
    public TimeConversion TimeConversion { get; init; }
    public string? TimeClockType { get; init; }
    public string? TimeDurationType { get; init; }
    public SpecialValueKind SpecialValue { get; init; }
    public string? SpecialValueType { get; init; }
    public CallableAbiInfo? CallableInfo { get; init; }
    public CallableOp CallableOp { get; init; }
}

internal enum CallableOp
{
    None,
    Invoke,
}

internal sealed class CallableAbiInfo
{
    public string NativeType { get; init; } = "";
    public string Surface { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string NamespaceName => $"Hosihikari.Bindings.{TypeNames.SurfaceNamespace(Surface)}.Interop";
    public string WrapperType => "global::" + NamespaceName + "." + ClassName;
    public AbiCallback Signature { get; init; } = new();
    public AbiFunction? InvokeFunction { get; set; }
    public AbiFunction? DestroyFunction { get; set; }
}

internal sealed class CallableRegistry
{
    private readonly Dictionary<(string Surface, string Type), CallableAbiInfo> _byKey = [];

    public IEnumerable<CallableAbiInfo> All => _byKey.Values;

    public bool TryGet(string surface, string type, out CallableAbiInfo info) =>
        _byKey.TryGetValue((surface, type), out info!);

    public void Add(CallableAbiInfo info) => _byKey[(info.Surface, info.NativeType)] = info;
}

internal sealed class VariantRegistry
{
    private readonly Dictionary<(string Surface, string Type), VariantAbiInfo> _byKey = [];

    public IEnumerable<VariantAbiInfo> All => _byKey.Values;

    public bool TryGet(string surface, string type, out VariantAbiInfo info) =>
        _byKey.TryGetValue((surface, type), out info!);

    public void Add(VariantAbiInfo info) => _byKey[(info.Surface, info.NativeType)] = info;
}

internal sealed class AbiPlan
{
    private static readonly string[] CallbackTypePrefixes =
    [
        "std::function<",
        "brstd::move_only_function<",
        "brstd::function_ref<",
    ];

    private AbiPlan(
        IReadOnlyList<AbiFunction> functions,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        bool linkCarrier,
        bool llAllocator,
        bool virtualDispatch,
        bool symbolDispatch,
        IReadOnlyDictionary<string, string> skipReasons,
        IReadOnlyDictionary<string, IReadOnlyList<MemberRecord>> syntheticMembers,
        IReadOnlyList<VariantAbiInfo> usedVariants,
        IReadOnlyList<CallableAbiInfo> usedCallables)
    {
        Functions = functions;
        SyntheticMembers = syntheticMembers;
        DeclarationNames = declarationNames;
        LinkCarrier = linkCarrier;
        LlAllocator = llAllocator;
        VirtualDispatch = virtualDispatch;
        SymbolDispatch = symbolDispatch;
        SkipReasons = skipReasons;
        UsedVariants = usedVariants;
        UsedCallables = usedCallables;
        CreateByMember = functions
            .Where(function => function.Kind == AbiFunctionKind.Create && function.MemberId is not null)
            .ToDictionary(function => function.MemberId!, StringComparer.Ordinal);
        GetByMember = functions
            .Where(function => function.Kind == AbiFunctionKind.Get && function.MemberId is not null)
            .ToDictionary(function => function.MemberId!, StringComparer.Ordinal);
        SetByMember = functions
            .Where(function => function.Kind == AbiFunctionKind.Set && function.MemberId is not null)
            .ToDictionary(function => function.MemberId!, StringComparer.Ordinal);
        MethodByMember = functions
            .Where(function => function.Kind == AbiFunctionKind.Method && function.MemberId is not null)
            .ToDictionary(function => function.MemberId!, StringComparer.Ordinal);
        DestroyByDeclaration = functions
            .Where(function => function.Kind == AbiFunctionKind.Destroy)
            .ToDictionary(function => function.DeclarationId, StringComparer.Ordinal);
        FunctionsByDeclaration = functions
            .GroupBy(function => function.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    public IReadOnlyList<AbiFunction> Functions { get; }
    public IReadOnlyDictionary<string, DeclarationName> DeclarationNames { get; }
    public bool LinkCarrier { get; }
    public bool LlAllocator { get; }
    public bool VirtualDispatch { get; }
    public bool SymbolDispatch { get; }
    public IReadOnlyDictionary<string, string> SkipReasons { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<MemberRecord>> SyntheticMembers { get; }
    public IReadOnlyList<VariantAbiInfo> UsedVariants { get; }
    public IReadOnlyList<CallableAbiInfo> UsedCallables { get; }

    public bool TryGetSkipReason(string memberId, out string reason) =>
        SkipReasons.TryGetValue(memberId, out reason!);
    public IReadOnlyDictionary<string, AbiFunction> CreateByMember { get; }
    public IReadOnlyDictionary<string, AbiFunction> GetByMember { get; }
    public IReadOnlyDictionary<string, AbiFunction> SetByMember { get; }
    public IReadOnlyDictionary<string, AbiFunction> MethodByMember { get; }
    public IReadOnlyDictionary<string, AbiFunction> DestroyByDeclaration { get; }
    public IReadOnlyDictionary<string, List<AbiFunction>> FunctionsByDeclaration { get; }

    public static AbiPlan Empty { get; } = new([], new Dictionary<string, DeclarationName>(StringComparer.Ordinal), false, false, false, false, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<MemberRecord>>(StringComparer.Ordinal), [], []);

    public IEnumerable<AbiFunction> FunctionsForSurface(string surface) =>
        Functions.Where(function => string.Equals(function.Surface, surface, StringComparison.Ordinal));

    public bool HasMemberFunction(string memberId) =>
        CreateByMember.ContainsKey(memberId)
        || GetByMember.ContainsKey(memberId)
        || SetByMember.ContainsKey(memberId)
        || MethodByMember.ContainsKey(memberId);

    public static AbiPlan Build(
        IReadOnlyList<IrRecord> records,
        string? nativeRoot,
        bool linkCarrier = false,
        bool llAllocator = false,
        bool virtualDispatch = false,
        bool symbolDispatch = false)
    {
        if (!symbolDispatch
            && (string.IsNullOrWhiteSpace(nativeRoot) || !Directory.Exists(nativeRoot)))
        {
            return Empty;
        }

        var allClassDeclarations = records.OfType<DeclarationRecord>()
            .Where(declaration => declaration.Kind is "class" or "struct" or "union")
            .OrderBy(declaration => declaration.Id, StringComparer.Ordinal)
            .ToList();
        var declarationNames = CSharpNaming.BuildDeclarationNames(allClassDeclarations);
        var publicDeclarationsByQualifiedName = allClassDeclarations
            .Where(declaration => declaration.Access == "public")
            .GroupBy(declaration => declaration.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var allDeclarationsByQualifiedName = records.OfType<DeclarationRecord>()
            .GroupBy(declaration => declaration.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var abstractDeclarationIds = records.OfType<MemberRecord>()
            .Where(member => member.Pure)
            .Select(member => member.DeclarationId)
            .ToHashSet(StringComparer.Ordinal);
        var enumUnderlyingByName = records.OfType<DeclarationRecord>()
            .Where(declaration => declaration.Kind == "enum"
                && !string.IsNullOrWhiteSpace(declaration.UnderlyingType))
            .GroupBy(declaration => declaration.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().UnderlyingType!, StringComparer.Ordinal);

        var membersByDeclaration = records.OfType<MemberRecord>()
            .GroupBy(member => member.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(member => member.Id, StringComparer.Ordinal)
                    .Select(byId => byId.First())
                    .OrderBy(member => member.Id, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var symbolDestroyableDeclarationIds = symbolDispatch
            ? membersByDeclaration
                .Where(pair => pair.Value.Any(member =>
                    member.Kind == "destructor"
                    && member.Access == "public"
                    && (!string.IsNullOrWhiteSpace(member.Symbol)
                        || member.Rva is not null
                        || member.Address is not null)))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var layoutDeclarationIds = records.OfType<VariantRecord>()
            .Where(variant => variant.Layout is { Size: > 0 })
            .Select(variant => variant.DeclarationId)
            .ToHashSet(StringComparer.Ordinal);
        var layoutByDeclarationId = records.OfType<VariantRecord>()
            .Where(variant => variant.Layout is { Size: > 0 })
            .GroupBy(variant => variant.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Layout!, StringComparer.Ordinal);
        var basesByDeclarationForFields = records.OfType<VariantRecord>()
            .GroupBy(variant => variant.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(variant => variant.Bases.Count > 0
                        ? variant.Bases
                        : variant.Layout?.Bases ?? [])
                    .Where(baseInfo => baseInfo.Access == "public" && !baseInfo.Virtual)
                    .GroupBy(baseInfo => baseInfo.DeclarationId, StringComparer.Ordinal)
                    .Select(byId => byId.First())
                    .ToList(),
                StringComparer.Ordinal);
        var syntheticMembers = new Dictionary<string, List<MemberRecord>>(StringComparer.Ordinal);
        var symbolByValueReturns = new Dictionary<string, DeclarationRecord>(StringComparer.Ordinal);
        var symbolByValueCopyRequired = new Dictionary<string, bool>(StringComparer.Ordinal);
        var symbolVectorElements = new Dictionary<string, DeclarationRecord>(StringComparer.Ordinal);
        var symbolOptionalRecords = new Dictionary<string, DeclarationRecord>(StringComparer.Ordinal);
        if (symbolDispatch)
        {
            foreach (var candidate in publicDeclarationsByQualifiedName.Values)
            {
                if (candidate.Kind is not ("class" or "struct" or "union")
                    || !layoutByDeclarationId.TryGetValue(candidate.Id, out var layout)
                    || layout.Size <= 0
                    || candidate.Abstract)
                {
                    continue;
                }

                var candidateMembers = membersByDeclaration.TryGetValue(candidate.Id, out var value) ? value : [];

                // The generated headers carry some templates as empty shells whose real
                // layout only exists in the game binary. A by-value result sized from such
                // a shell would be too small, so a template may only carry one when its
                // header proves the layout with a data member or a base record.
                if (candidate.Template is not null
                    && !candidateMembers.Any(member => member.Kind == "field")
                    && layout.Bases.Count == 0)
                {
                    continue;
                }

                var hasVirtual = candidateMembers.Any(member => member.Virtual);
                if (candidate.TriviallyDestructible)
                {
                    if (candidate.TriviallyCopyable)
                    {
                        // Trivial records use the register path when small and the memory path
                        // otherwise.
                        if (hasVirtual || (layout.Size <= 8 && layout.Size is not (1 or 2 or 4 or 8)))
                        {
                            continue;
                        }
                    }
                    else if (hasVirtual)
                    {
                        // A user-provided copy constructor makes the record non-trivial for
                        // calls, so it always uses the memory convention.
                        continue;
                    }
                }
                else
                {
                    // Direct heap sret constructs the result in the caller-provided buffer,
                    // so only the destructor symbol is required to release it.
                    var destructor = candidateMembers.FirstOrDefault(member =>
                        member.Kind == "destructor"
                        && member.Access == "public"
                        && HasSymbolAddress(member));
                    if (destructor is null)
                    {
                        continue;
                    }
                }

                symbolByValueReturns[candidate.QualifiedName] = candidate;
                symbolByValueCopyRequired[candidate.QualifiedName] = !candidate.TriviallyDestructible;
            }

            // Clang prints a template type without its defaulted trailing arguments, so a
            // signature says Bedrock::Result<void> while the dependency record is
            // Bedrock::Result<void, std::error_code>. Bind the short spelling to the same
            // record so by-value returns and parameters reach its layout and destructor.
            foreach (var (qualifiedName, declaration) in symbolByValueReturns.ToList())
            {
                var shortName = ShortDefaultedResultSpelling(qualifiedName);
                if (shortName is not null)
                {
                    symbolByValueReturns.TryAdd(shortName, declaration);
                    symbolByValueCopyRequired.TryAdd(shortName, symbolByValueCopyRequired[qualifiedName]);
                }
            }

            // Vector elements are copied as bytes and released without running a destructor.
            // A user-provided copy constructor such as AABB's is compatible with that rule as
            // long as the record's storage is trivially destructible.
            foreach (var candidate in publicDeclarationsByQualifiedName.Values)
            {
                if (candidate.Kind is not ("class" or "struct" or "union")
                    || candidate.Abstract
                    || !candidate.TriviallyDestructible
                    || !layoutByDeclarationId.TryGetValue(candidate.Id, out var vectorLayout)
                    || vectorLayout.Size <= 0)
                {
                    continue;
                }

                symbolVectorElements[candidate.QualifiedName] = candidate;
            }

            // Optional payloads are byte-copied into a layout-compatible blob, so the record
            // must be trivially copyable for the optional to keep the trivial call ABI.
            foreach (var candidate in publicDeclarationsByQualifiedName.Values)
            {
                if (candidate.Kind is not ("class" or "struct" or "union")
                    || candidate.Abstract
                    || !candidate.TriviallyCopyable
                    || !layoutByDeclarationId.TryGetValue(candidate.Id, out var optionalLayout)
                    || optionalLayout.Size <= 0)
                {
                    continue;
                }

                symbolOptionalRecords[candidate.QualifiedName] = candidate;
            }
        }

        // A member that carries only a mangled name is still reachable: the carrier
        // resolves it by name at run time.
        static bool HasSymbolAddress(MemberRecord member) =>
            !string.IsNullOrWhiteSpace(member.Symbol)
            || member.Address is not null
            || member.Rva is not null
            || !string.IsNullOrWhiteSpace(member.MangledName);

        static bool HasDispatchEntry(MemberRecord member) =>
            HasSymbolAddress(member)
            || (member.Virtual && member.VTableIndex is not null);

        var skipReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new List<DeclarationRecord>();
        var byValueReturnNames = records
            .OfType<MemberRecord>()
            .Where(member => member.Signature is not null)
            .Select(member => NormalizeTypeName(member.Signature!.ReturnType))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var declaration in allClassDeclarations.Where(declaration => declaration.Access == "public"))
        {
            string? declarationReason = null;
            if (symbolDispatch)
            {
                // A template specialization that is returned by value keeps the declaration so
                // the shim can size its result buffer and emit the destroy entry; its members
                // still go through the ordinary member rules. The signature may print the
                // short Bedrock::Result<T> spelling while the declaration carries the
                // defaulted error argument, so both spellings are accepted. The header must
                // prove the layout; a reconstructed shell would size the buffer wrongly.
                var templateLayoutProven = declaration.Template is null
                    || (layoutByDeclarationId.TryGetValue(declaration.Id, out var templateLayout)
                        && (templateLayout.Bases.Count > 0
                            || (membersByDeclaration.TryGetValue(declaration.Id, out var templateMembers)
                                && templateMembers.Any(member => member.Kind == "field"))));
                var byValueReturnTemplate = declaration.Template is not null
                    && templateLayoutProven
                    && (byValueReturnNames.Contains(declaration.QualifiedName)
                        || (ShortDefaultedResultSpelling(declaration.QualifiedName) is { } shortResultName
                            && byValueReturnNames.Contains(shortResultName)));
                if (declaration.Template is not null && !byValueReturnTemplate)
                {
                    declarationReason = "template classes are not supported by symbol dispatch";
                }
                else if (!IsValidCppQualifiedName(declaration.QualifiedName))
                {
                    declarationReason = "the native name is not a valid C++ qualified name";
                }
                else if (HasMsvcInternalTypeName(declaration.QualifiedName))
                {
                    declarationReason = "the native name uses implementation-specific standard library types";
                }
            }
            else if (!linkCarrier && !virtualDispatch && declaration.Template is not null)
            {
                declarationReason = "template instances require --link-carrier";
            }
            else if (!IsValidCppQualifiedName(declaration.QualifiedName))
            {
                declarationReason = "the native name is not a valid C++ qualified name";
            }
            else if (HasMsvcInternalTypeName(declaration.QualifiedName))
            {
                declarationReason = "the native name uses implementation-specific standard library types";
            }
            else if (declaration.Template is not null
                && ((linkCarrier || virtualDispatch)
                    ? HasInaccessibleTemplateArgument(declaration)
                        || HasUnsafeIncompleteTemplateArgument(declaration)
                    : !AreTemplateArgumentsUsable(declaration, allDeclarationsByQualifiedName)))
            {
                declarationReason = "the template arguments are inaccessible or incomplete";
            }
            else if (declaration.Source is null)
            {
                declarationReason = "the source location is unavailable";
            }
            else if (!IsNativeSourceAvailable(nativeRoot, declaration.Source.File))
            {
                declarationReason = "the source file is unavailable in the native root";
            }

            if (declarationReason is null)
            {
                declarations.Add(declaration);
                continue;
            }

            if (membersByDeclaration.TryGetValue(declaration.Id, out var declarationMembers))
            {
                foreach (var member in declarationMembers.Where(member => member.Access == "public"))
                {
                    skipReasons[member.Id] = declarationReason;
                }
            }
        }

        var allDeclarationsById = records.OfType<DeclarationRecord>()
            .GroupBy(declaration => declaration.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var basesByDeclaration = records.OfType<VariantRecord>()
            .GroupBy(variant => variant.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(variant => variant.Layout?.Bases ?? [])
                    .Select(baseInfo => baseInfo.DeclarationId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);
        var publicBaseInfosByDeclaration = records.OfType<VariantRecord>()
            .GroupBy(variant => variant.DeclarationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(variant => variant.Layout?.Bases ?? [])
                    .Where(baseInfo => baseInfo.Access == "public")
                    .GroupBy(baseInfo => baseInfo.DeclarationId)
                    .Select(byId => byId.First())
                    .ToList(),
                StringComparer.Ordinal);
        var polymorphicDeclarationIds = records.OfType<MemberRecord>()
            .Where(member => member.Virtual || member.Pure)
            .Select(member => member.DeclarationId)
            .ToHashSet(StringComparer.Ordinal);
        var linkSafeMemo = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool IsLinkSafe(string declarationId)
        {
            if (linkSafeMemo.TryGetValue(declarationId, out var cached))
            {
                return cached;
            }

            if (!allDeclarationsById.ContainsKey(declarationId))
            {
                linkSafeMemo[declarationId] = false;
                return false;
            }

            // Break inheritance cycles conservatively.
            linkSafeMemo[declarationId] = false;

            if (membersByDeclaration.TryGetValue(declarationId, out var members)
                && members.Any(member =>
                    (member.Kind is "method" or "operator" or "constructor" or "destructor")
                    && !member.Inline))
            {
                return false;
            }

            if (basesByDeclaration.TryGetValue(declarationId, out var bases))
            {
                foreach (var baseId in bases)
                {
                    if (!IsLinkSafe(baseId))
                    {
                        return false;
                    }
                }
            }

            linkSafeMemo[declarationId] = true;
            return true;
        }

        bool IsDestructorUsable(DeclarationRecord declaration, List<MemberRecord> declarationMembers)
        {
            var destructor = declarationMembers.FirstOrDefault(member => member.Kind == "destructor");
            if (symbolDispatch)
            {
                return destructor is null
                    || (destructor.Access == "public" && HasSymbolAddress(destructor));
            }

            var hasOutOfLineMethod = declarationMembers.Any(member =>
                (member.Kind is "method" or "operator" or "constructor" or "destructor")
                && !member.Inline);
            return linkCarrier
                ? destructor is null
                    || (destructor.Access == "public"
                        && HasExportedSymbol(declaration, destructor)
                        && (declaration.Template is null || destructor.Defined))
                : destructor is null
                    ? !hasOutOfLineMethod
                    : destructor.Access == "public" && destructor.Inline && !hasOutOfLineMethod;
        }

        bool HasImplicitSpecializationMembers(DeclarationRecord declaration, List<MemberRecord> declarationMembers) =>
            declarationMembers.Any(member =>
                (member.Signature?.Canonical.StartsWith(declaration.QualifiedName + "<", StringComparison.Ordinal) ?? false)
                || member.Id.Contains(declaration.QualifiedName + "<", StringComparison.Ordinal));

        bool CanDeleteImplicitDestructor(
            DeclarationRecord declaration,
            List<MemberRecord> declarationMembers,
            out bool explicitDestructor)
        {
            explicitDestructor = false;
            var destructor = declarationMembers.FirstOrDefault(member => member.Kind == "destructor");
            if (symbolDispatch)
            {
                explicitDestructor = destructor is not null
                    && destructor.Access == "public"
                    && HasSymbolAddress(destructor);
                return destructor is null || explicitDestructor;
            }

            if (destructor is not null)
            {
                explicitDestructor = destructor.Access == "public"
                    && (linkCarrier ? HasExportedSymbol(declaration, destructor) : destructor.Inline)
                    && (declaration.Template is null || destructor.Defined);
                return explicitDestructor;
            }

            // An implicit destructor is instantiated at the delete site. Member smart pointers to
            // incomplete implementation types make that instantiation ill-formed.
            if (declarationMembers.Any(member =>
                    member.Kind == "field"
                    && member.Type is not null
                    && member.Type.Canonical.StartsWith("std::unique_ptr<", StringComparison.Ordinal)))
            {
                return false;
            }

            var constructors = declarationMembers.Where(member => member.Kind == "constructor").ToList();
            return constructors.Count == 0
                || constructors.Any(member => member.Access == "public");
        }

        var allowOwnedByValue = linkCarrier || virtualDispatch;
        var uniquePtrOwnedDeclarationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in records.OfType<MemberRecord>())
        {
            if (member.Signature is null)
            {
                continue;
            }

            var returnType = member.Signature.ReturnType;
            string uniquePointee;
            if (TryParseUniquePtr(returnType, out var directPointee))
            {
                uniquePointee = directPointee;
            }
            else if (TryParseVector(returnType, out var vectorElement)
                && TryParseUniquePtr(vectorElement, out var nestedPointee))
            {
                uniquePointee = nestedPointee;
            }
            else
            {
                continue;
            }

            if (!allDeclarationsByQualifiedName.TryGetValue(uniquePointee, out var uniqueDeclaration)
                || uniqueDeclaration.Access != "public")
            {
                continue;
            }

            uniquePtrOwnedDeclarationIds.Add(uniqueDeclaration.Id);
        }

        // A variant alternative can be a class whose members are all inherited (for example
        // StepSlider, which re-exports Dropdown through `using Dropdown::Dropdown;`). The IR
        // records no members for it, but a copy made by the variant wrapper still needs a
        // destroy function. Limit this path to memberless classes that actually appear as
        // variant alternatives so unrelated declarations stay untouched.
        var memberlessVariantAlternativeIds = allowOwnedByValue
            ? CollectMemberlessVariantAlternatives(records, membersByDeclaration, publicDeclarationsByQualifiedName)
            : new HashSet<string>(StringComparer.Ordinal);
        var destroyableDeclarationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in allowOwnedByValue ? declarations : [])
        {
            if (!membersByDeclaration.TryGetValue(declaration.Id, out var declarationMembers))
            {
                if (!memberlessVariantAlternativeIds.Contains(declaration.Id))
                {
                    continue;
                }

                declarationMembers = [];
            }

            if (HasImplicitSpecializationMembers(declaration, declarationMembers)
                || !(linkCarrier || virtualDispatch || IsLinkSafe(declaration.Id))
                || (declaration.Abstract && !uniquePtrOwnedDeclarationIds.Contains(declaration.Id))
                || !CanDeleteImplicitDestructor(declaration, declarationMembers, out _))
            {
                continue;
            }

            destroyableDeclarationIds.Add(declaration.Id);
        }

        var variants = BuildVariantRegistry(
            records,
            publicDeclarationsByQualifiedName,
            declarationNames,
            destroyableDeclarationIds,
            enumUnderlyingByName,
            allowOwnedByValue);
        var callables = BuildCallableRegistry(records, enumUnderlyingByName);

        var functions = new List<AbiFunction>();
        var uncastableDeclarationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (!membersByDeclaration.TryGetValue(declaration.Id, out var declarationMembers))
            {
                // Header-derived dependency records carry no members. A by-value eligible
                // record still needs its destroy entry because returned copies are owned.
                if (!destroyableDeclarationIds.Contains(declaration.Id)
                    && !(symbolDispatch && symbolByValueReturns.ContainsKey(declaration.QualifiedName)))
                {
                    continue;
                }

                declarationMembers = [];
            }

            // A primary-template record can carry the members of an implicit specialization.
            // Its native name would then be incomplete (no template arguments), so it is not a
            // safe C++ type for the shim until the IR distinguishes the two.
            if (HasImplicitSpecializationMembers(declaration, declarationMembers))
            {
                uncastableDeclarationIds.Add(declaration.Id);
                continue;
            }

            // A class that stores a string_view/reference field keeps pointers into its
            // constructor arguments. The shim backs those arguments with holder-owned strings
            // so the native object remains valid after the call.
            var ownsBorrowedString = declarationMembers.Any(member =>
                member.Kind == "field"
                && member.Type is not null
                && (IsStringViewType(member.Type.Canonical)
                    || (IsReferenceType(member.Type.Canonical)
                        && !IsRValueReference(member.Type.Canonical)
                        && IsStringType(member.Type.Canonical))));
            var linkSafe = symbolDispatch || linkCarrier || virtualDispatch || IsLinkSafe(declaration.Id);
            var destructorUsable = IsDestructorUsable(declaration, declarationMembers);
            var hasPublicDestructor = linkSafe
                && (symbolDispatch || !virtualDispatch)
                && (!symbolDispatch || layoutDeclarationIds.Contains(declaration.Id))
                && CanDeleteImplicitDestructor(
                    declaration,
                    declarationMembers,
                    out var explicitDestructor)
                && explicitDestructor;
            var destructorAccessible = linkSafe && destructorUsable;
            var isAbstract = declaration.Abstract || declarationMembers.Any(member => member.Pure);
            var hasAbstractTemplateArgument = declaration.Template is not null
                && HasAbstractTemplateArgument(declaration, allDeclarationsByQualifiedName, abstractDeclarationIds);
            var created = false;

            foreach (var constructor in declarationMembers.Where(member =>
                         member.Kind == "constructor"
                         && member.Access == "public"
                         && !member.Id.Contains("@support:", StringComparison.Ordinal)))
            {
                string? reason = null;
                if (symbolDispatch)
                {
                    if (!HasSymbolAddress(constructor))
                    {
                        reason = "the constructor has no symbol in the IR";
                    }
                    else if (!layoutDeclarationIds.Contains(declaration.Id))
                    {
                        reason = "the class layout is unavailable";
                    }
                    else if (ownsBorrowedString)
                    {
                        reason = "classes that store borrowed strings are not supported by symbol dispatch";
                    }
                }
                else if (!(virtualDispatch ? constructor.Inline
                        : linkCarrier ? HasExportedSymbol(declaration, constructor)
                        : constructor.Inline))
                {
                    reason = virtualDispatch
                        ? "the constructor requires linking against the carrier"
                        : "the constructor is not exported by the carrier";
                }
                else if (linkCarrier
                    && declaration.Template is not null
                    && constructor.Inline
                    && !constructor.Defined)
                {
                    reason = "the template constructor has no visible definition";
                }
                else if (!virtualDispatch && constructor.Pure)
                {
                    reason = "the constructor is pure virtual";
                }
                else if (!destructorAccessible)
                {
                    reason = "the class has no accessible destructor";
                }
                else if (isAbstract)
                {
                    reason = "the class is abstract";
                }
                else if (hasAbstractTemplateArgument && !ownsBorrowedString)
                {
                    reason = "the class has an abstract template argument";
                }

                if (reason is not null)
                {
                    skipReasons[constructor.Id] = reason;
                    continue;
                }

                if (!TryBuildCreate(
                        declaration,
                        constructor,
                        publicDeclarationsByQualifiedName,
                        declarationNames,
                        destroyableDeclarationIds,
                        ownsBorrowedString,
                        enumUnderlyingByName,
                        allowOwnedByValue,
                        symbolDispatch,
                        variants,
                        out var create,
                        out reason))
                {
                    skipReasons[constructor.Id] = reason ?? "the constructor cannot be projected";
                    continue;
                }

                if (symbolDispatch && !IsSymbolDispatchSupported(create, symbolByValueReturns, symbolVectorElements, symbolOptionalRecords, out reason))
                {
                    skipReasons[constructor.Id] = reason ?? "the constructor is not supported by symbol dispatch";
                    continue;
                }

                functions.Add(create);
                created = true;
            }

            var hasDestroy = created
                || (hasPublicDestructor && !virtualDispatch)
                || (allowOwnedByValue && destroyableDeclarationIds.Contains(declaration.Id))
                || (symbolDispatch && symbolByValueReturns.ContainsKey(declaration.QualifiedName));
            if (hasDestroy)
            {
                functions.Add(BuildDestroy(declaration, declarationNames, ownsBorrowedString));
            }
            else
            {
                var destructor = declarationMembers.FirstOrDefault(member =>
                    member.Kind == "destructor" && member.Access == "public");
                if (destructor is not null)
                {
                    skipReasons[destructor.Id] = "the class has no accessible destructor";
                }
            }

            foreach (var field in declarationMembers.Where(member =>
                         (member.Kind == "field" || member.Kind == "static_field")
                         && member.Access == "public"))
            {
                if (field.Kind == "static_field"
                    && ConstantFields.TryCSharpConstant(field, out _, out _))
                {
                    // The projection is the constant itself; no shim entry is needed.
                    continue;
                }

                if (symbolDispatch)
                {
                    if (!TryPlanSymbolField(
                            declaration,
                            field,
                            field.BitOffset ?? -1,
                            field.Id,
                            declarationNames,
                            publicDeclarationsByQualifiedName,
                            enumUnderlyingByName,
                            out var symbolGetter,
                            out var symbolSetter,
                            out var symbolFieldReason))
                    {
                        skipReasons[field.Id] = symbolFieldReason ?? "the field is not supported by symbol dispatch";
                        continue;
                    }

                    functions.Add(symbolGetter);
                    if (symbolSetter is not null)
                    {
                        functions.Add(symbolSetter);
                    }

                    continue;
                }

                if (field.VariableTemplate)
                {
                    skipReasons[field.Id] = "static variable templates are not projected";
                    continue;
                }

                if (field.Kind == "static_field"
                    && !linkCarrier
                    && !(virtualDispatch && field.Inline))
                {
                    skipReasons[field.Id] = "the static field requires linking against the carrier";
                    continue;
                }

                if (field.Type is null)
                {
                    skipReasons[field.Id] = "the field type is unavailable";
                    continue;
                }

                if (!TryBuildField(
                        declaration,
                        field,
                        publicDeclarationsByQualifiedName,
                        declarationNames,
                        destroyableDeclarationIds,
                        enumUnderlyingByName,
                        allowOwnedByValue,
                        variants,
                        out var getter,
                        out var setter,
                        out var fieldReason))
                {
                    skipReasons[field.Id] = fieldReason ?? "the field type is not supported";
                    continue;
                }

                functions.Add(getter);
                if (setter is not null)
                {
                    functions.Add(setter);
                }
            }

            if (symbolDispatch)
            {
                var inheritedFields = new Dictionary<string, (MemberRecord Field, long BitOffset)>(StringComparer.Ordinal);
                CollectInheritedSymbolFields(
                    declaration.Id,
                    0,
                    new HashSet<string>(StringComparer.Ordinal) { declaration.Id },
                    0,
                    basesByDeclarationForFields,
                    membersByDeclaration,
                    inheritedFields);

                foreach (var (baseField, bitOffset) in inheritedFields.Values.OrderBy(pair => pair.BitOffset))
                {
                    var syntheticId = baseField.Id + "::inherited::" + declaration.Id;
                    if (!TryPlanSymbolField(
                            declaration,
                            baseField,
                            bitOffset,
                            syntheticId,
                            declarationNames,
                            publicDeclarationsByQualifiedName,
                            enumUnderlyingByName,
                            out var inheritedGetter,
                            out var inheritedSetter,
                            out _))
                    {
                        continue;
                    }

                    if (!syntheticMembers.TryGetValue(declaration.Id, out var members))
                    {
                        members = [];
                        syntheticMembers[declaration.Id] = members;
                    }

                    members.Add(new MemberRecord
                    {
                        Id = syntheticId,
                        DeclarationId = declaration.Id,
                        Kind = "field",
                        Name = baseField.Name,
                        Access = "public",
                        Static = false,
                        Type = baseField.Type,
                        BitOffset = bitOffset,
                        BitWidth = 0,
                    });
                    functions.Add(inheritedGetter);
                    if (inheritedSetter is not null)
                    {
                        functions.Add(inheritedSetter);
                    }
                }
            }

            foreach (var method in declarationMembers.Where(member =>
                         member.Kind is "method" or "operator" && member.Access == "public"))
            {
                string? reason = null;
                if (symbolDispatch)
                {
                    if (!HasDispatchEntry(method))
                    {
                        reason = "the member has no symbol or vtable slot in the IR";
                    }
                    else if (method.Pure && !(method.Virtual && method.VTableIndex is not null))
                    {
                        // A pure declaration has no implementation symbol, but a vtable slot
                        // dispatches through the object's override.
                        reason = "the member is pure virtual";
                    }
                }
                else if (!linkSafe)
                {
                    reason = "the class is not link-safe in the current mode";
                }
                else if (!(virtualDispatch
                    ? method.Inline || method.Virtual
                    : linkCarrier ? HasExportedSymbol(declaration, method) : method.Inline))
                {
                    reason = virtualDispatch
                        ? "the member requires linking against the carrier"
                        : "the member is not exported by the carrier";
                }
                else if (!virtualDispatch && method.Pure)
                {
                    reason = "the member is pure virtual";
                }
                else if (method.MemberTemplate)
                {
                    reason = "member function templates are not projected";
                }
                else if (string.Equals(method.Name, "operator=", StringComparison.Ordinal)
                    && declaration.Template is not null
                    && !method.Defined)
                {
                    // A template specialization whose assignment operator has no visible
                    // definition cannot be compiled by the shim; a defined one can.
                    reason = "the template assignment operator has no visible definition";
                }

                if (reason is not null)
                {
                    skipReasons[method.Id] = reason;
                    continue;
                }

                if (!TryBuildMethod(
                        declaration,
                        method,
                        publicDeclarationsByQualifiedName,
                        declarationNames,
                        destroyableDeclarationIds,
                        enumUnderlyingByName,
                        allowOwnedByValue,
                        symbolDispatch,
                        symbolDestroyableDeclarationIds,
                        symbolByValueReturns,
                        symbolByValueCopyRequired,
                        symbolVectorElements,
                        symbolOptionalRecords,
                        variants,
                        callables,
                        out var function,
                        out reason))
                {
                    skipReasons[method.Id] = reason ?? "the member cannot be projected";
                    continue;
                }

                if (symbolDispatch && !IsSymbolDispatchSupported(function, symbolByValueReturns, symbolVectorElements, symbolOptionalRecords, out reason))
                {
                    skipReasons[method.Id] = reason ?? "the member is not supported by symbol dispatch";
                    continue;
                }

                functions.Add(function);
            }
        }

        // Upcasts and RTTI downcasts are synthetic table functions. They alias the same object
        // and always produce borrowed wrappers; no ownership is transferred.
        var castableDeclarationIds = symbolDispatch
            ? new HashSet<string>(StringComparer.Ordinal)
            : declarations
                .Select(declaration => declaration.Id)
                .Where(id => !uncastableDeclarationIds.Contains(id))
                .ToHashSet(StringComparer.Ordinal);
        var castNamesByDeclaration = declarations.ToDictionary(
            declaration => declaration.Id,
            declaration => new HashSet<string>(
                membersByDeclaration.TryGetValue(declaration.Id, out var declarationMembers)
                    ? declarationMembers
                        .Where(member => member.Access == "public")
                        .Select(member => TypeNames.Sanitize(member.Name))
                    : [],
                StringComparer.Ordinal),
            StringComparer.Ordinal);

        string CastMemberName(string ownerId, string candidate, string targetId)
        {
            var names = castNamesByDeclaration[ownerId];
            if (names.Add(candidate))
            {
                return candidate;
            }

            var suffixed = candidate + "_" + CSharpNaming.ShortHash(targetId);
            names.Add(suffixed);
            return suffixed;
        }

        var castPairs = new List<(DeclarationRecord Derived, DeclarationRecord Base)>();
        var emittedCastPairs = new HashSet<(string Derived, string Base)>();
        foreach (var declaration in declarations)
        {
            if (!castableDeclarationIds.Contains(declaration.Id)
                || !publicBaseInfosByDeclaration.TryGetValue(declaration.Id, out var baseInfos))
            {
                continue;
            }

            var pending = new Stack<string>(baseInfos.Select(baseInfo => baseInfo.DeclarationId));
            var visited = new HashSet<string>(StringComparer.Ordinal) { declaration.Id };
            while (pending.Count > 0)
            {
                var baseId = pending.Pop();
                if (!visited.Add(baseId)
                    || !castableDeclarationIds.Contains(baseId)
                    || !allDeclarationsById.TryGetValue(baseId, out var baseDeclaration)
                    || baseDeclaration.Kind is not ("class" or "struct" or "union")
                    || baseDeclaration.Access != "public"
                    || !string.Equals(baseDeclaration.Surface, declaration.Surface, StringComparison.Ordinal)
                    || !declarationNames.ContainsKey(baseId))
                {
                    continue;
                }

                if (baseDeclaration.QualifiedName.StartsWith("std::", StringComparison.Ordinal))
                {
                    continue;
                }

                if (emittedCastPairs.Add((declaration.Id, baseId)))
                {
                    castPairs.Add((declaration, baseDeclaration));
                }

                if (publicBaseInfosByDeclaration.TryGetValue(baseId, out var nextBases))
                {
                    foreach (var next in nextBases)
                    {
                        pending.Push(next.DeclarationId);
                    }
                }
            }
        }

        foreach (var (declaration, baseDeclaration) in castPairs)
        {
            if (!declarationNames.TryGetValue(declaration.Id, out var derivedName)
                || !declarationNames.TryGetValue(baseDeclaration.Id, out var baseName))
            {
                continue;
            }

            var upcastId = declaration.Id + "::cast::up::" + baseDeclaration.Id;
            functions.Add(new AbiFunction
            {
                CppName = "hs_cast_" + CSharpNaming.ShortHash(upcastId),
                Kind = AbiFunctionKind.Cast,
                CastKind = AbiCastKind.Upcast,
                Surface = declaration.Surface,
                DeclarationId = declaration.Id,
                MemberId = upcastId,
                ClassCppName = declaration.QualifiedName,
                ClassName = derivedName,
                TargetCppName = baseDeclaration.QualifiedName,
                TargetDeclarationId = baseDeclaration.Id,
                NativeMemberName = "",
                CSharpMemberName = CastMemberName(declaration.Id, "As" + baseName.ClassName, baseDeclaration.Id),
                ReturnKind = AbiKind.Opaque,
                NativeReturnType = baseDeclaration.QualifiedName + " *",
                ReturnWrapperType = "global::" + baseName.NamespaceName + "." + baseName.ClassName,
            });

            if (!polymorphicDeclarationIds.Contains(baseDeclaration.Id))
            {
                continue;
            }

            var downcastId = baseDeclaration.Id + "::cast::down::" + declaration.Id;
            functions.Add(new AbiFunction
            {
                CppName = "hs_cast_" + CSharpNaming.ShortHash(downcastId),
                Kind = AbiFunctionKind.Cast,
                CastKind = AbiCastKind.Downcast,
                Surface = baseDeclaration.Surface,
                DeclarationId = baseDeclaration.Id,
                MemberId = downcastId,
                ClassCppName = baseDeclaration.QualifiedName,
                ClassName = baseName,
                TargetCppName = declaration.QualifiedName,
                TargetDeclarationId = declaration.Id,
                NativeMemberName = "",
                CSharpMemberName = CastMemberName(baseDeclaration.Id, "TryAs" + derivedName.ClassName, declaration.Id),
                ReturnKind = AbiKind.Opaque,
                NativeReturnType = declaration.QualifiedName + " *",
                ReturnWrapperType = "global::" + derivedName.NamespaceName + "." + derivedName.ClassName,
                ReturnNullable = true,
            });
        }

        // A director is generated for an abstract class with exactly one pure virtual method
        // returning void, taking one reference/pointer, and a public virtual destructor.
        // The constructor must use only scalar, enum, or shared parameters so the factory
        // can forward them without extra prelude.
        static bool IsDirectorConstructorParameter(AbiParameter parameter) =>
            !parameter.IsOptional
            && !parameter.IsUniquePtrParam
            && !parameter.IsVector
            && !parameter.IsMap
            && (parameter.Kind == AbiKind.Shared
                || parameter.Kind is AbiKind.Bool or AbiKind.I8 or AbiKind.U8
                    or AbiKind.I16 or AbiKind.U16 or AbiKind.I32 or AbiKind.U32
                    or AbiKind.I64 or AbiKind.U64 or AbiKind.USize or AbiKind.ISize
                    or AbiKind.F32 or AbiKind.F64);

        static bool IsDirectorCallbackParameter(AbiParameter parameter)
        {
            if (parameter.IsOptional || parameter.IsUniquePtrParam || parameter.IsVector || parameter.IsMap)
            {
                return false;
            }

            if (parameter.Kind == AbiKind.Opaque)
            {
                var core = StripTopLevelQualifiers(parameter.NativeType);
                var isPointer = core.EndsWith("*", StringComparison.Ordinal);
                var isReference = core.EndsWith("&", StringComparison.Ordinal)
                    && !core.EndsWith("&&", StringComparison.Ordinal);
                if (!isPointer && !isReference)
                {
                    return false;
                }

                var pointee = StripTopLevelQualifiers(core[..^1].TrimEnd());
                return !pointee.EndsWith("*", StringComparison.Ordinal)
                    && !pointee.EndsWith("&", StringComparison.Ordinal);
            }

            return parameter.Kind is AbiKind.Bool or AbiKind.I8 or AbiKind.U8
                or AbiKind.I16 or AbiKind.U16 or AbiKind.I32 or AbiKind.U32
                or AbiKind.I64 or AbiKind.U64 or AbiKind.USize or AbiKind.ISize
                or AbiKind.F32 or AbiKind.F64;
        }

        foreach (var declaration in declarations)
        {
            if (!castableDeclarationIds.Contains(declaration.Id)
                || declaration.Template is not null
                || !membersByDeclaration.TryGetValue(declaration.Id, out var directorMembers))
            {
                continue;
            }

            var pureMethods = directorMembers
                .Where(member => member.Pure
                    && member.Kind == "method"
                    && member.Access is "public" or "protected")
                .ToList();
            if (pureMethods.Count != 1)
            {
                continue;
            }

            var callMethod = pureMethods[0];
            var callSignature = callMethod.Signature;
            if (callSignature is null || callSignature.Variadic)
            {
                continue;
            }

            var callbackParameters = new List<AbiParameter>();
            var callbackUsable = true;
            foreach (var parameter in callSignature.Parameters)
            {
                if (!TryMapParameter(
                        declaration.Surface,
                        parameter.Name,
                        parameter.Type,
                        publicDeclarationsByQualifiedName,
                        declarationNames,
                        destroyableDeclarationIds,
                        enumUnderlyingByName,
                        linkCarrier,
                        symbolDispatch,
                        variants,
                        out var mapped,
                        out _)
                    || !IsDirectorCallbackParameter(mapped))
                {
                    callbackUsable = false;
                    break;
                }

                callbackParameters.Add(mapped);
            }

            if (!callbackUsable)
            {
                continue;
            }

            var returnText = callSignature.ReturnType.Trim();
            AbiKind directorReturnKind;
            if (string.Equals(returnText, "void", StringComparison.Ordinal))
            {
                directorReturnKind = AbiKind.Void;
            }
            else if (!(TryMapEnumUnderlying(returnText, enumUnderlyingByName, out directorReturnKind)
                || TryMapFundamental(returnText, out directorReturnKind)))
            {
                continue;
            }

            var destructor = directorMembers.FirstOrDefault(member => member.Kind == "destructor");
            if (destructor is null
                || !destructor.Virtual
                || destructor.Access != "public"
                || !(virtualDispatch
                    ? destructor.Inline
                    : linkCarrier ? HasExportedSymbol(declaration, destructor) : destructor.Inline))
            {
                continue;
            }

            var directorParameters = new List<AbiParameter>();
            var hasConstructor = !directorMembers.Any(member => member.Kind == "constructor");
            foreach (var constructor in directorMembers
                         .Where(member => member.Kind == "constructor"
                             && member.Access is "public" or "protected")
                         .OrderBy(member => member.Id, StringComparer.Ordinal))
            {
                if (constructor.Signature is null || constructor.Signature.Variadic)
                {
                    continue;
                }

                if (!(virtualDispatch
                        ? constructor.Inline
                        : linkCarrier ? HasExportedSymbol(declaration, constructor) : constructor.Inline))
                {
                    continue;
                }

                var parameters = new List<AbiParameter>();
                var usable = true;
                foreach (var parameter in constructor.Signature.Parameters)
                {
                    if (!TryMapParameter(
                            declaration.Surface,
                            parameter.Name,
                            parameter.Type,
                            publicDeclarationsByQualifiedName,
                            declarationNames,
                            destroyableDeclarationIds,
                            enumUnderlyingByName,
                            linkCarrier,
                            symbolDispatch,
                            variants,
                            out var mapped,
                            out _)
                        || !IsDirectorConstructorParameter(mapped))
                    {
                        usable = false;
                        break;
                    }

                    parameters.Add(mapped);
                }

                if (!usable)
                {
                    continue;
                }

                directorParameters = parameters;
                hasConstructor = true;
                break;
            }

            if (!hasConstructor)
            {
                continue;
            }

            var constructorCount = directorParameters.Count;
            directorParameters.Add(new AbiParameter { Name = "invoke", Kind = AbiKind.Opaque, NativeType = "void*" });
            directorParameters.Add(new AbiParameter { Name = "userData", Kind = AbiKind.Opaque, NativeType = "void*" });
            directorParameters.Add(new AbiParameter { Name = "release", Kind = AbiKind.Opaque, NativeType = "void*" });

            var directorId = declaration.Id + "::director";
            functions.Add(new AbiFunction
            {
                CppName = "hs_director_" + CSharpNaming.ShortHash(directorId),
                Kind = AbiFunctionKind.DirectorCreate,
                Surface = declaration.Surface,
                DeclarationId = declaration.Id,
                MemberId = directorId,
                ClassCppName = declaration.QualifiedName,
                ClassName = declarationNames[declaration.Id],
                NativeMemberName = callMethod.Name,
                CSharpMemberName = CastMemberName(declaration.Id, "CreateDirector", callMethod.Id),
                ReturnKind = AbiKind.Shared,
                NativeReturnType = "std::shared_ptr<" + declaration.QualifiedName + ">",
                SharedWrapper = SharedWrapperKind.SharedPtr,
                ReturnNullable = true,
                Parameters = directorParameters,
                Static = true,
                DirectorConstructorParameterCount = constructorCount,
                DirectorCallbackParameters = callbackParameters,
                DirectorReturnKind = directorReturnKind,
                DirectorReturnNativeType = returnText,
                DirectorMethodIsConst = callSignature.Const,
            });
        }

        // Namespace-level functions are emitted as static C# classes. An inline body compiles
        // into the shim; a carrier export can also be called through the import library, so
        // out-of-line functions are eligible when link-carrier is on.
        static bool IsFreeFunctionParameterSupported(AbiParameter parameter)
        {
            if (parameter.Kind == AbiKind.StringView)
            {
                return !IsNonConstLValueReference(parameter.NativeType);
            }

            if (parameter.Kind != AbiKind.Opaque)
            {
                // Vectors, optionals, unique pointers, maps, shared handles, callbacks and
                // scalars project through the same argument builder the member path uses.
                return true;
            }

            var core = StripTopLevelQualifiers(parameter.NativeType);
            var isPointer = core.EndsWith("*", StringComparison.Ordinal);
            var isReference = core.EndsWith("&", StringComparison.Ordinal)
                && !core.EndsWith("&&", StringComparison.Ordinal);
            if (!isPointer && !isReference)
            {
                // A by-value record crosses through its copy constructor.
                return IsValidCppQualifiedName(core);
            }

            var pointee = StripTopLevelQualifiers(core[..^1].TrimEnd());
            return !pointee.EndsWith("*", StringComparison.Ordinal)
                && !pointee.EndsWith("&", StringComparison.Ordinal)
                && IsValidCppQualifiedName(pointee);
        }

        if (linkCarrier || virtualDispatch)
        {
            var occupiedFunctionTypeNames = declarationNames.Values
                .Select(name => name.NamespaceName + "." + name.ClassName)
                .ToHashSet(StringComparer.Ordinal);
            var functionClassNamesByNamespace = new Dictionary<string, DeclarationName>(StringComparer.Ordinal);
            var freeFunctionCandidates = new List<(DeclarationRecord Function, DeclarationName ClassName, string BaseName)>();

            foreach (var function in records.OfType<DeclarationRecord>()
                         .Where(declaration => declaration.Kind == "function")
                         .OrderBy(declaration => declaration.Id, StringComparer.Ordinal))
            {
                // In virtual-dispatch mode the shim compiles the umbrella that declares the
                // function; a body recorded as not visible is still in that translation unit
                // when the declaration is inline.
                var callableInline = function.Inline && (function.Defined || virtualDispatch);
                var callableImported = linkCarrier
                    && (HasExportAttribute(function.Attributes) || HasExportMacro(function.Macros));
                // A mapped comparison operator has an identifier name in C#, so validate the
                // projected name rather than the native spelling.
                var nameParts = TypeNames.SplitQualifiedName(function.QualifiedName).ToList();
                var mappedOperatorName = nameParts.Count > 0
                    && nameParts[^1].StartsWith("operator", StringComparison.Ordinal)
                        ? OperatorMethodName(nameParts[^1])
                        : null;
                var validName = nameParts.Count > 0
                    && IsValidCppQualifiedName(string.Join(
                        "::",
                        nameParts.Take(nameParts.Count - 1).Append(mappedOperatorName ?? nameParts[^1])));
                if (function.Access != "public"
                    || function.Template is not null
                    || function.Signature is null
                    || function.Signature.Variadic
                    || function.Consteval
                    || (!callableInline && !callableImported)
                    || function.Source is null
                    || !validName
                    || HasMsvcInternalTypeName(function.QualifiedName)
                    || HasMsvcInternalTypeName(function.Signature!.ReturnType)
                    || HasUnnameableTypeName(function.Signature.ReturnType)
                    || function.Signature.Parameters.Any(parameter =>
                        HasMsvcInternalTypeName(parameter.Type) || HasUnnameableTypeName(parameter.Type))
                    || !IsNativeSourceAvailable(nativeRoot, function.Source.File))
                {
                    continue;
                }

                var parts = TypeNames.SplitQualifiedName(function.QualifiedName).ToList();
                if (parts.Count == 0)
                {
                    continue;
                }

                var nativeName = parts[^1];
                var baseName = nativeName.StartsWith("operator", StringComparison.Ordinal)
                    ? OperatorMethodName(nativeName)
                    : TypeNames.Sanitize(nativeName);
                if (string.IsNullOrEmpty(baseName))
                {
                    continue;
                }

                var surfaceNamespace = "Hosihikari.Bindings." + TypeNames.SurfaceNamespace(function.Surface) + ".Interop";
                var namespaceName = parts.Count > 1
                    ? surfaceNamespace + "." + string.Join(".", parts.Take(parts.Count - 1).Select(TypeNames.Sanitize))
                    : surfaceNamespace;

                if (!functionClassNamesByNamespace.TryGetValue(namespaceName, out var className))
                {
                    var candidate = "Functions";
                    if (!occupiedFunctionTypeNames.Add(namespaceName + "." + candidate))
                    {
                        candidate = "NativeFunctions";
                        if (!occupiedFunctionTypeNames.Add(namespaceName + "." + candidate))
                        {
                            candidate = "NativeFunctions_" + CSharpNaming.ShortHash(namespaceName);
                            occupiedFunctionTypeNames.Add(namespaceName + "." + candidate);
                        }
                    }

                    className = new DeclarationName(namespaceName, namespaceName, candidate);
                    functionClassNamesByNamespace[namespaceName] = className;
                }

                freeFunctionCandidates.Add((function, className, baseName));
            }

            foreach (var group in freeFunctionCandidates.GroupBy(
                         candidate => (candidate.ClassName.NamespaceName, candidate.ClassName.ClassName, candidate.BaseName)))
            {
                var duplicate = group.Count() > 1;
                foreach (var candidate in group)
                {
                    if (!TryMapReturnType(
                            candidate.Function.Surface,
                            candidate.Function.Signature!.ReturnType,
                            publicDeclarationsByQualifiedName,
                            declarationNames,
                            destroyableDeclarationIds,
                            enumUnderlyingByName,
                            linkCarrier,
                            symbolDispatch,
                            symbolDestroyableDeclarationIds,
                            null,
                            variants,
                            callables,
                            out var returnKind,
                            out var nativeReturnType,
                            out var returnWrapperType,
                            out var returnOwned,
                            out var returnUniquePtr,
                            out var returnOptional,
                            out var returnArray,
                            out var returnSpan,
                            out var arrayElementKind,
                            out var arrayElementType,
                            out var arrayElementSharedWrapper,
                            out var arrayElementOwnership,
                            out var arrayElementWrapperType,
                            out var arrayElementNativeType,
                            out var sharedWrapper,
                            out var timeConversion,
                            out var timeClockType,
                            out var timeDurationType,
                            out var specialValue,
                            out var specialValueType,
                            out var mapKeyKind,
                            out var mapValueKind,
                            out var mapValueWrapperType,
                            out var mapValueOwnership,
                            out var mapValueNativeType,
                            out var returnNullable,
                            out var returnExpected,
                            out var expectedErrorType,
                            out var returnVariant,
                            out var returnCallable,
                            out _))
                    {
                        continue;
                    }

                    var parameters = new List<AbiParameter>();
                    var usable = true;
                    foreach (var parameter in candidate.Function.Signature.Parameters)
                    {
                        if (!TryMapParameter(
                                candidate.Function.Surface,
                                parameter.Name,
                                parameter.Type,
                                publicDeclarationsByQualifiedName,
                                declarationNames,
                                destroyableDeclarationIds,
                                enumUnderlyingByName,
                                linkCarrier,
                                symbolDispatch,
                                variants,
                                out var mapped,
                                out _)
                            || !IsFreeFunctionParameterSupported(mapped))
                        {
                            usable = false;
                            break;
                        }

                        parameters.Add(mapped);
                    }

                    if (!usable)
                    {
                        continue;
                    }

                    var nativeLeaf = TypeNames.SplitQualifiedName(candidate.Function.QualifiedName);
                    var operatorLeaf = nativeLeaf.Count > 0
                        && nativeLeaf[^1].StartsWith("operator", StringComparison.Ordinal)
                        && OperatorMethodName(nativeLeaf[^1]) is not null
                            ? nativeLeaf[^1]
                            : null;

                    functions.Add(new AbiFunction
                    {
                        CppName = "hs_function_" + CSharpNaming.ShortHash(candidate.Function.Id),
                        Kind = AbiFunctionKind.FreeFunction,
                        Surface = candidate.Function.Surface,
                        DeclarationId = candidate.Function.Id,
                        MemberId = candidate.Function.Id,
                        ClassCppName = candidate.Function.QualifiedName,
                        ClassName = candidate.ClassName,
                        // A comparison operator may be a hidden friend of a class template;
                        // the unqualified name lets ADL find it.
                        NativeMemberName = operatorLeaf ?? candidate.Function.QualifiedName,
                        CSharpMemberName = duplicate
                            ? candidate.BaseName + "_" + CSharpNaming.ShortHash(candidate.Function.Id)
                            : candidate.BaseName,
                        ReturnKind = returnKind,
                        NativeReturnType = nativeReturnType,
                        ReturnWrapperType = returnWrapperType,
                        ReturnOwned = returnOwned,
                        ReturnUniquePtr = returnUniquePtr,
                        ReturnOptional = returnOptional,
                        ReturnArray = returnArray,
                        ReturnSpan = returnSpan,
                        ArrayElementKind = arrayElementKind,
                        ArrayElementType = arrayElementType,
                        ArrayElementSharedWrapper = arrayElementSharedWrapper,
                        ArrayElementOwnership = arrayElementOwnership,
                        ArrayElementWrapperType = arrayElementWrapperType,
                        ArrayElementNativeType = arrayElementNativeType,
                        SharedWrapper = sharedWrapper,
                        TimeConversion = timeConversion,
                        TimeClockType = timeClockType,
                        TimeDurationType = timeDurationType,
                        SpecialValue = specialValue,
                        SpecialValueType = specialValueType,
                        MapKeyKind = mapKeyKind,
                        MapValueKind = mapValueKind,
                        MapValueWrapperType = mapValueWrapperType,
                        MapValueOwnership = mapValueOwnership,
                        MapValueNativeType = mapValueNativeType,
                        ReturnNullable = returnNullable,
                        ReturnExpected = returnExpected,
                        ExpectedErrorType = expectedErrorType,
                        VariantInfo = returnVariant,
                        CallableInfo = returnCallable,
                        Parameters = parameters,
                        Static = true,
                    });
                }
            }
        }

        if (symbolDispatch)
        {
            // A by-value vector return copies record elements into owned, aligned blocks. The
            // managed array constructs owning wrappers, so each element type needs a destroy
            // entry even when the class has no constructors or other owned path.
            var destroyableIds = functions
                .Where(function => function.Kind == AbiFunctionKind.Destroy)
                .Select(function => function.DeclarationId)
                .ToHashSet(StringComparer.Ordinal);
            var ownedElementNames = functions
                .Where(function => function.ReturnArray
                    && function.ArrayElementOwnership == ArrayElementOwnership.RecordCopy)
                .Select(function => function.ArrayElementNativeType)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var elementName in ownedElementNames)
            {
                if (!symbolVectorElements.TryGetValue(elementName, out var elementDeclaration)
                    || !declarationNames.ContainsKey(elementDeclaration.Id)
                    || !destroyableIds.Add(elementDeclaration.Id))
                {
                    continue;
                }

                functions.Add(BuildDestroy(elementDeclaration, declarationNames, false));
            }
        }

        var usedVariants = functions
            .SelectMany(function => function.Parameters
                .Select(parameter => parameter.VariantInfo)
                .Append(function.VariantInfo))
            .OfType<VariantAbiInfo>()
            .Distinct()
            .ToList();
        foreach (var variant in usedVariants)
        {
            variant.Used = true;
            functions.AddRange(BuildVariantFunctions(variant));
        }

        var usedCallables = functions
            .Select(function => function.CallableInfo)
            .OfType<CallableAbiInfo>()
            .Distinct()
            .ToList();
        foreach (var callable in usedCallables)
        {
            functions.AddRange(BuildCallableFunctions(callable));
        }

        // Table field names use the first 32 bits of a SHA-256; with tens of thousands of
        // functions a collision is possible and would define one C symbol twice. Extend
        // the colliding name deterministically so both sides keep the same spelling.
        var usedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            if (usedFunctionNames.Add(function.CppName))
            {
                continue;
            }

            var suffix = 1;
            string uniqueName;
            do
            {
                uniqueName = function.CppName + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                suffix++;
            }
            while (!usedFunctionNames.Add(uniqueName));

            function.CppName = uniqueName;
        }

        var ordered = functions
            .GroupBy(function => function.Surface, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderBy(function => function.DeclarationId, StringComparer.Ordinal)
                .ThenBy(function => function.MemberId, StringComparer.Ordinal)
                .ThenBy(function => function.Kind)
                .ThenBy(function => function.CppName, StringComparer.Ordinal))
            .ToList();

        var indexBySurface = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var function in ordered)
        {
            indexBySurface.TryGetValue(function.Surface, out var index);
            function.Index = index;
            indexBySurface[function.Surface] = index + 1;
        }

        return new AbiPlan(
            ordered,
            declarationNames,
            linkCarrier,
            llAllocator,
            virtualDispatch,
            symbolDispatch,
            skipReasons,
            syntheticMembers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<MemberRecord>)pair.Value,
                StringComparer.Ordinal),
            usedVariants,
            usedCallables);
    }

    public static string CppType(AbiKind kind) => kind switch
    {
        AbiKind.Void => "void",
        AbiKind.Bool => "bool",
        AbiKind.I8 => "std::int8_t",
        AbiKind.U8 => "std::uint8_t",
        AbiKind.I16 => "std::int16_t",
        AbiKind.U16 => "std::uint16_t",
        AbiKind.I32 => "std::int32_t",
        AbiKind.U32 => "std::uint32_t",
        AbiKind.I64 => "std::int64_t",
        AbiKind.U64 => "std::uint64_t",
        AbiKind.USize => "std::size_t",
        AbiKind.ISize => "std::ptrdiff_t",
        AbiKind.F32 => "float",
        AbiKind.F64 => "double",
        AbiKind.Opaque => "void*",
        AbiKind.StringView => "HosihikariStringView",
        AbiKind.Callback => "void*",
        AbiKind.ArrayView => "HosihikariArrayView",
        AbiKind.Vector => "void",
        AbiKind.Shared => "HosihikariSharedHandle",
        AbiKind.MapView => "HosihikariArrayView",
        _ => "void",
    };

    public static string CSharpType(AbiKind kind) => kind switch
    {
        AbiKind.Void => "void",
        AbiKind.Bool => "bool",
        AbiKind.I8 => "sbyte",
        AbiKind.U8 => "byte",
        AbiKind.I16 => "short",
        AbiKind.U16 => "ushort",
        AbiKind.I32 => "int",
        AbiKind.U32 => "uint",
        AbiKind.I64 => "long",
        AbiKind.U64 => "ulong",
        AbiKind.USize => "nuint",
        AbiKind.ISize => "nint",
        AbiKind.F32 => "float",
        AbiKind.F64 => "double",
        AbiKind.Opaque => "nint",
        AbiKind.StringView => "string",
        AbiKind.Callback => "nint",
        AbiKind.ArrayView => "nint",
        AbiKind.Vector => "nint",
        AbiKind.Shared => "nint",
        AbiKind.MapView => "nint",
        _ => "nint",
    };

    public static string CSharpTableType(AbiKind kind) => kind switch
    {
        // Managed bool returns are normalized to 0/1; the native side only guarantees
        // the low byte of the return register, so read it as a byte and compare.
        AbiKind.Bool => "byte",
        AbiKind.Opaque => "void*",
        AbiKind.StringView => "global::Hosihikari.Bindings.Abi.StringView",
        AbiKind.ArrayView => "global::Hosihikari.Bindings.Abi.ArrayView",
        AbiKind.Vector => "nint",
        AbiKind.Shared => "global::Hosihikari.Bindings.Abi.SharedHandleData",
        AbiKind.MapView => "global::Hosihikari.Bindings.Abi.ArrayView",
        _ => CSharpType(kind),
    };

    public static string CSharpParameterType(AbiParameter parameter) =>
        parameter.IsMap
            ? $"global::System.Collections.Generic.Dictionary<{CSharpMapKeyType(parameter.MapKeyKind)}, {CSharpMapValueType(parameter)}>"
            : parameter.Kind switch
            {
                AbiKind.StringView => "string",
                AbiKind.Opaque => parameter.WrapperType is not null && parameter.IsPointer
                    ? parameter.WrapperType + "?"
                    : parameter.WrapperType ?? "nint",
                AbiKind.Shared => "global::Hosihikari.Bindings.Abi.SharedHandle?",
                _ => CSharpType(parameter.Kind),
            };

    public static string CSharpMapKeyType(AbiKind kind) => kind switch
    {
        AbiKind.StringView => "string",
        AbiKind.Opaque => "nint",
        _ => CSharpType(kind),
    };

    public static string CSharpMapValueType(AbiParameter parameter) => parameter.MapValueKind switch
    {
        AbiKind.StringView => "string",
        AbiKind.Opaque => parameter.MapValueOwnership == ArrayElementOwnership.None
            ? "nint"
            : parameter.MapValueWrapperType!,
        _ => CSharpType(parameter.MapValueKind),
    };

    public static string CSharpPropertyType(AbiKind kind) => kind switch
    {
        AbiKind.StringView => "string",
        AbiKind.Opaque => "nint",
        _ => CSharpType(kind),
    };

    public static string CppParameterType(AbiParameter parameter) => parameter.Kind switch
    {
        AbiKind.Opaque => "void*",
        AbiKind.StringView => "const char*, std::size_t",
        _ => CppType(parameter.Kind),
    };

    public static bool IsStringViewType(string? canonical)
    {
        if (canonical is null)
        {
            return false;
        }

        if (StripTopLevelQualifiers(canonical).EndsWith("*", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = NormalizeStandardTypeSpelling(NormalizeTypeName(canonical));
        return string.Equals(normalized, "std::basic_string_view<char>", StringComparison.Ordinal)
            || string.Equals(normalized, "std::string_view", StringComparison.Ordinal);
    }

    internal static bool IsStringType(string canonical)
    {
        if (StripTopLevelQualifiers(canonical).EndsWith("*", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = NormalizeStandardTypeSpelling(NormalizeTypeName(canonical));
        return string.Equals(normalized, "std::basic_string<char>", StringComparison.Ordinal)
            || string.Equals(normalized, "std::string", StringComparison.Ordinal);
    }

    /// <summary>
    /// Symbol records spell default template arguments explicitly, for example
    /// <c>std::basic_string&lt;char,std::char_traits&lt;char&gt;,std::allocator&lt;char&gt;&gt;</c>.
    /// They denote the same type as the shortened header spelling.
    /// </summary>
    private static string NormalizeStandardTypeSpelling(string type)
    {
        var compact = type.Replace(" ", "", StringComparison.Ordinal);
        return compact switch
        {
            "std::basic_string<char,std::char_traits<char>,std::allocator<char>>" => "std::basic_string<char>",
            "std::basic_string_view<char,std::char_traits<char>>" => "std::basic_string_view<char>",
            _ => compact,
        };
    }

    private static string NormalizeTypeName(string canonical)
    {
        var text = StripTopLevelQualifiers(canonical);
        while (text.EndsWith("&", StringComparison.Ordinal) || text.EndsWith("*", StringComparison.Ordinal))
        {
            text = StripTopLevelQualifiers(text[..^1].TrimEnd());
        }

        return text;
    }

    /// <summary>The symbol demangler writes a space after template commas; the IR ids do not.</summary>
    internal static string CompactTemplateName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @",\s+", ",");

    private static string StripTopLevelQualifiers(string canonical)
    {
        var text = canonical.Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            if (text.StartsWith("const ", StringComparison.Ordinal))
            {
                text = text["const ".Length..].TrimStart();
                changed = true;
            }

            if (text.StartsWith("volatile ", StringComparison.Ordinal))
            {
                text = text["volatile ".Length..].TrimStart();
                changed = true;
            }

            if (text.EndsWith(" const", StringComparison.Ordinal))
            {
                text = text[..^" const".Length].TrimEnd();
                changed = true;
            }

            if (text.EndsWith(" volatile", StringComparison.Ordinal))
            {
                text = text[..^" volatile".Length].TrimEnd();
                changed = true;
            }

            // A qualifier on a pointer itself is spelled without a space (`T*const`); drop
            // it so the caller still sees the pointer.
            if (text.EndsWith("const", StringComparison.Ordinal)
                && text.Length > "const".Length
                && text[^("const".Length + 1)] is '*' or '&')
            {
                text = text[..^"const".Length];
                changed = true;
            }

            if (text.EndsWith("volatile", StringComparison.Ordinal)
                && text.Length > "volatile".Length
                && text[^("volatile".Length + 1)] is '*' or '&')
            {
                text = text[..^"volatile".Length];
                changed = true;
            }
        }

        return text;
    }

    private static bool HasExportedSymbol(DeclarationRecord declaration, MemberRecord member) =>
        member.Inline
        || HasExportAttribute(declaration.Attributes)
        || HasExportAttribute(member.Attributes)
        || HasExportMacro(declaration.Macros)
        || HasExportMacro(member.Macros);

    private static bool HasExportAttribute(IEnumerable<AttributeInfo> attributes) =>
        attributes.Any(attribute =>
            attribute.Name.Equals("dllexport", StringComparison.OrdinalIgnoreCase)
            || attribute.Name.Equals("dllimport", StringComparison.OrdinalIgnoreCase));

    private static bool HasExportMacro(IEnumerable<MacroUseInfo> macros) =>
        macros.Any(macro => macro.Name is "LLAPI" or "LLNDAPI" or "LLCAPI" or "MCAPI" or "MCNAPI" or "ENDSTONE_EVENT");

    internal static string NormalizeTemplateTypeName(string value) => NormalizeTypeName(value);

    internal static bool IsFundamentalTemplateArgument(string type) =>
        TryMapFundamental(type, out _) || IsScalarAliasType(type);

    internal static bool IsStandardOrThirdPartyType(string type) =>
        type.StartsWith("std::", StringComparison.Ordinal)
        || type.StartsWith("gsl::", StringComparison.Ordinal)
        || type.StartsWith("fmt::", StringComparison.Ordinal)
        || type.StartsWith("entt::", StringComparison.Ordinal)
        || type.StartsWith("nonstd::", StringComparison.Ordinal)
        || type.StartsWith("expected_lite::", StringComparison.Ordinal)
        || type.StartsWith("boost::", StringComparison.Ordinal)
        || type.StartsWith("cereal::", StringComparison.Ordinal)
        || type.StartsWith("phmap::", StringComparison.Ordinal)
        || type.StartsWith("magic_enum::", StringComparison.Ordinal)
        || type.StartsWith("type_safe::", StringComparison.Ordinal);

    internal static bool IsLiteralTemplateArgument(string text)
    {
        var value = text.Trim();
        if (value is "true" or "false" or "nullptr" or "NULL")
        {
            return true;
        }

        value = value.TrimStart('-', '+');
        if (value.Length == 0)
        {
            return false;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > 2 && value.Skip(2).All(Uri.IsHexDigit);
        }

        return value.All(current =>
            char.IsDigit(current) || current is '.' or 'U' or 'u' or 'L' or 'l' or 'F' or 'f');
    }

    internal static string? ExtractTemplateArguments(string type)
    {
        var open = type.IndexOf('<');
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var index = open; index < type.Length; index++)
        {
            if (type[index] == '<')
            {
                depth++;
            }
            else if (type[index] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return index == type.Length - 1 ? type[(open + 1)..index] : null;
                }
            }
        }

        return null;
    }

    internal static IEnumerable<string> SplitTemplateArguments(string inner)
    {
        if (inner.Length == 0)
        {
            yield break;
        }

        var depth = 0;
        var start = 0;
        for (var index = 0; index < inner.Length; index++)
        {
            var current = inner[index];
            if (current == '<')
            {
                depth++;
            }
            else if (current == '>')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                yield return inner[start..index].Trim();
                start = index + 1;
            }
        }

        yield return inner[start..].Trim();
    }

    private static bool IsScalarAliasType(string type) => type is
        "uint8" or "uint16" or "uint32" or "uint64"
        or "int8" or "int16" or "int32" or "int64"
        or "uchar" or "ushort" or "uint" or "ulong" or "ulonglong"
        or "schar" or "ssize_t" or "intptr_t" or "uintptr_t"
        or "wchar_t" or "char8_t" or "char16_t" or "char32_t";

    private static bool HasAbstractTemplateArgument(
        DeclarationRecord declaration,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        HashSet<string> abstractDeclarationIds)
    {
        if (declaration.Template is null)
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in declaration.Template.Arguments)
        {
            if (string.Equals(argument.Kind, "pack", StringComparison.Ordinal))
            {
                foreach (var part in PackArgumentParts(argument.Value))
                {
                    if (IsAbstractTemplateType(part, declarationsByQualifiedName, abstractDeclarationIds, visited))
                    {
                        return true;
                    }
                }
            }
            else if (string.Equals(argument.Kind, "type", StringComparison.Ordinal)
                && IsAbstractTemplateType(argument.Value, declarationsByQualifiedName, abstractDeclarationIds, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAbstractTemplateType(
        string value,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        HashSet<string> abstractDeclarationIds,
        HashSet<string> visited)
    {
        var type = NormalizeTypeName(value);
        if (string.IsNullOrWhiteSpace(type) || !visited.Add(type))
        {
            return false;
        }

        if (declarationsByQualifiedName.TryGetValue(type, out var declaration))
        {
            if (abstractDeclarationIds.Contains(declaration.Id))
            {
                return true;
            }

            if (declaration.Template is not null)
            {
                foreach (var argument in declaration.Template.Arguments)
                {
                    if (string.Equals(argument.Kind, "type", StringComparison.Ordinal)
                        && IsAbstractTemplateType(
                            argument.Value,
                            declarationsByQualifiedName,
                            abstractDeclarationIds,
                            visited))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        if (!IsStandardOrThirdPartyType(type))
        {
            return false;
        }

        var inner = ExtractTemplateArguments(type);
        if (inner is null)
        {
            return false;
        }

        return SplitTemplateArguments(inner).Any(part =>
            IsAbstractTemplateType(part, declarationsByQualifiedName, abstractDeclarationIds, visited));
    }

    private static bool HasInaccessibleTemplateArgument(DeclarationRecord declaration) =>
        declaration.Template?.Arguments.Any(argument =>
            argument.Accessible.HasValue && !argument.Accessible.Value) ?? false;

    private static bool HasUnsafeIncompleteTemplateArgument(DeclarationRecord declaration) =>
        declaration.Template?.Arguments.Any(argument =>
            argument.Complete.HasValue
            && !argument.Complete.Value
            && !IsStandardOrThirdPartyType(NormalizeTypeName(argument.Value))) ?? false;

    private static bool AreTemplateArgumentsUsable(
        DeclarationRecord declaration,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName)
    {
        if (declaration.Template is null)
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in declaration.Template.Arguments)
        {
            if (string.Equals(argument.Kind, "pack", StringComparison.Ordinal))
            {
                foreach (var part in PackArgumentParts(argument.Value))
                {
                    if (!IsUsableTemplateType(part, declarationsByQualifiedName, visited))
                    {
                        return false;
                    }
                }
            }
            else if (string.Equals(argument.Kind, "type", StringComparison.Ordinal)
                && !IsUsableTemplateType(argument.Value, declarationsByQualifiedName, visited))
            {
                return false;
            }
        }

        return true;
    }

    internal static IEnumerable<string> PackArgumentParts(string value)
    {
        var inner = ExtractTemplateArguments(value);
        return inner is null ? [value] : SplitTemplateArguments(inner);
    }

    private static bool IsUsableTemplateType(
        string value,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        HashSet<string> visited)
    {
        var type = NormalizeTypeName(value);
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        if (IsFundamentalTemplateArgument(type) || IsLiteralTemplateArgument(type))
        {
            return true;
        }

        if (!visited.Add(type))
        {
            return true;
        }

        if (declarationsByQualifiedName.TryGetValue(type, out var declaration))
        {
            if (declaration.Template is not null)
            {
                foreach (var argument in declaration.Template.Arguments)
                {
                    if (string.Equals(argument.Kind, "type", StringComparison.Ordinal)
                        && !IsUsableTemplateType(argument.Value, declarationsByQualifiedName, visited))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        if (IsStandardOrThirdPartyType(type))
        {
            var inner = ExtractTemplateArguments(type);
            if (inner is null)
            {
                return true;
            }

            foreach (var part in SplitTemplateArguments(inner))
            {
                if (!IsUsableTemplateType(part, declarationsByQualifiedName, visited))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool HasMsvcInternalTypeName(string text) =>
        text.Contains("std::_", StringComparison.Ordinal);

    private static bool HasUnnameableTypeName(string text) =>
        text.Contains("(lambda at ", StringComparison.Ordinal)
        || text.Contains("<lambda", StringComparison.Ordinal)
        || text.Contains("type-parameter-", StringComparison.Ordinal);

    /// <summary>
    /// Namespace-level comparison operators have no C# operator form, so they surface as
    /// static methods. Other operators stay unprojected.
    /// </summary>
    private static string? OperatorMethodName(string name) => name switch
    {
        "operator==" => "operator_equal",
        "operator!=" => "operator_not_equal",
        "operator<" => "operator_less",
        "operator<=" => "operator_less_equal",
        "operator>" => "operator_greater",
        "operator>=" => "operator_greater_equal",
        _ => null,
    };

    private static bool IsValidCppQualifiedName(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName)
            || qualifiedName.Contains("...", StringComparison.Ordinal)
            || qualifiedName.Contains("decltype", StringComparison.Ordinal)
            || qualifiedName.Contains("auto", StringComparison.Ordinal)
            || qualifiedName.Contains("__attribute", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = TypeNames.SplitQualifiedName(qualifiedName);
        if (parts.Count == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsValidCppNamePart(part))
            {
                return false;
            }
        }

        return true;
    }

    // Bedrock::Result<T, std::error_code> is printed as Bedrock::Result<T> in signatures
    // because clang suppresses defaulted template arguments. The short spelling reaches the
    // full dependency record, which carries the layout and the destructor symbol.
    private static string? ShortDefaultedResultSpelling(string qualifiedName)
    {
        const string prefix = "Bedrock::Result<";
        if (!qualifiedName.StartsWith(prefix, StringComparison.Ordinal)
            || !qualifiedName.EndsWith(">", StringComparison.Ordinal))
        {
            return null;
        }

        var arguments = SplitTopLevelTemplateArguments(qualifiedName[prefix.Length..^1]);
        return arguments.Count == 2 && arguments[1] == "std::error_code"
            ? prefix + arguments[0] + ">"
            : null;
    }

    private static List<string> SplitTopLevelTemplateArguments(string text)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        arguments.Add(text[start..].Trim());
        return arguments;
    }

    private static bool IsValidCppNamePart(string part)
    {
        var index = 0;
        if (!ReadCppIdentifier(part, ref index))
        {
            return false;
        }

        if (index == part.Length)
        {
            return true;
        }

        if (part[index] != '<')
        {
            return false;
        }

        var depth = 0;
        for (; index < part.Length; index++)
        {
            var current = part[index];
            if (current == '<')
            {
                depth++;
                continue;
            }

            if (current == '>')
            {
                if (depth == 0)
                {
                    return false;
                }

                depth--;
                continue;
            }

            if (depth == 0)
            {
                return false;
            }

            // A template argument may be a function type: function_traits<void (T)>.
            if (current is '(' or ')')
            {
                continue;
            }

            if (current is '{' or '}' or '[' or ']' or '"' or '\''
                or ';' or '=' or '@' or '\\' or '#' or '?' or '!' or '%' or '^'
                or '|' or '~' or '`')
            {
                return false;
            }

            if (!(char.IsLetterOrDigit(current)
                  || current is '_' or ':' or ',' or ' ' or '*' or '&' or '-' or '+' or '.'))
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static bool ReadCppIdentifier(string text, ref int index)
    {
        if (index >= text.Length || (text[index] != '_' && !char.IsLetter(text[index])))
        {
            return false;
        }

        index++;
        while (index < text.Length && (text[index] == '_' || char.IsLetterOrDigit(text[index])))
        {
            index++;
        }

        return true;
    }

    private static bool IsNativeSourceAvailable(string? nativeRoot, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(nativeRoot) || string.IsNullOrWhiteSpace(sourceFile))
        {
            return false;
        }

        var normalized = sourceFile.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) && File.Exists(normalized))
        {
            return true;
        }

        var fullPath = Path.GetFullPath(Path.Combine(nativeRoot, normalized.TrimStart('/')));
        return File.Exists(fullPath);
    }

    private static bool TryBuildCreate(
        DeclarationRecord declaration,
        MemberRecord constructor,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        bool ownsBorrowedString,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool linkCarrier,
        bool symbolDispatch,
        VariantRegistry variants,
        out AbiFunction function,
        out string? reason)
    {
        function = null!;
        reason = null;
        if (constructor.Signature is null)
        {
            reason = "the constructor signature is unavailable";
            return false;
        }

        if (constructor.Signature.Variadic)
        {
            reason = "variadic constructors are not supported";
            return false;
        }

        if (constructor.Consteval)
        {
            reason = "consteval constructors cannot be called at runtime";
            return false;
        }

        var parameters = new List<AbiParameter>();
        foreach (var parameter in constructor.Signature.Parameters)
        {
            if (!TryMapParameter(
                    declaration.Surface,
                    parameter.Name,
                    parameter.Type,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    enumUnderlyingByName,
                    linkCarrier || symbolDispatch,
                    symbolDispatch,
                    variants,
                    out var abiParameter,
                    out var parameterReason))
            {
                reason = parameterReason;
                return false;
            }

            if (abiParameter.Kind == AbiKind.StringView
                && IsNonConstLValueReference(parameter.Type))
            {
                // The shim would have to bind a temporary string to a mutable reference.
                reason = "constructors do not accept mutable string references";
                return false;
            }

            if (abiParameter.Kind == AbiKind.Opaque
                && abiParameter.IsReference
                && !abiParameter.IsConst
                && !symbolDispatch
                && !CanPassMutableReference(
                    declaration,
                    abiParameter,
                    declarationsByQualifiedName,
                    declarationNames))
            {
                // Copy-like constructors that take a mutable reference can instantiate
                // unrelated templates (for example AnyFunction(AnyFunction&)).
                reason = "constructors with mutable reference parameters are not projected";
                return false;
            }

            parameters.Add(abiParameter);
        }

        if (ownsBorrowedString)
        {
            var stringParameters = parameters
                .Select((parameter, index) => (Parameter: parameter, Type: constructor.Signature.Parameters[index].Type))
                .Where(pair => pair.Parameter.Kind == AbiKind.StringView)
                .ToList();
            if (stringParameters.Count == 0)
            {
                reason = "the class stores borrowed strings but the constructor has no string parameter";
                return false;
            }

            foreach (var pair in stringParameters)
            {
                if (!IsSafeBorrowedStringParameter(pair.Type))
                {
                    reason = "the class stores borrowed strings and the constructor takes a string by value";
                    return false;
                }
            }
        }

        function = new AbiFunction
        {
            CppName = "hs_create_" + CSharpNaming.ShortHash(constructor.Id),
            Kind = AbiFunctionKind.Create,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            MemberId = constructor.Id,
            ClassCppName = declaration.QualifiedName,
            ClassName = declarationNames[declaration.Id],
            NativeMemberName = constructor.Name,
            CSharpMemberName = "Create",
            ReturnKind = AbiKind.Opaque,
            NativeReturnType = "void*",
            Parameters = parameters,
            CreateViaBorrowedHolder = ownsBorrowedString,
        };
        return true;
    }

    private static bool IsSafeBorrowedStringParameter(string type)
    {
        var text = type.Trim();
        if (IsStringViewType(text))
        {
            return true;
        }

        if (IsPointerType(text) && text.Contains("char", StringComparison.Ordinal))
        {
            return true;
        }

        if (!IsReferenceType(text) || !IsConstType(text) || IsRValueReference(text))
        {
            return false;
        }

        var stripped = text;
        if (stripped.StartsWith("const ", StringComparison.Ordinal))
        {
            stripped = stripped["const ".Length..].TrimStart();
        }

        while (stripped.EndsWith("&", StringComparison.Ordinal))
        {
            stripped = stripped[..^1].TrimEnd();
        }

        return IsStringType(stripped);
    }

    private static bool CanPassMutableReference(
        DeclarationRecord declaration,
        AbiParameter parameter,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames)
    {
        if (declaration.Template is not null)
        {
            // A template constructor can bind a mutable reference to an unrelated implicit
            // specialization; keep those out of the projection.
            return false;
        }

        if (parameter.NativePointee is not { Length: > 0 } pointee
            || !declarationsByQualifiedName.TryGetValue(pointee, out var pointeeDeclaration))
        {
            return false;
        }

        return pointeeDeclaration.Access == "public"
            && declarationNames.ContainsKey(pointeeDeclaration.Id);
    }

    private static AbiFunction BuildDestroy(
        DeclarationRecord declaration,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        bool borrowed) =>
        new()
        {
            CppName = "hs_destroy_" + CSharpNaming.ShortHash(declaration.Id),
            Kind = AbiFunctionKind.Destroy,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            ClassCppName = declaration.QualifiedName,
            ClassName = declarationNames[declaration.Id],
            ReturnKind = AbiKind.Void,
            NativeReturnType = "void",
            DestroyViaBorrowedHolder = borrowed,
        };

    private static bool TryBuildMethod(
        DeclarationRecord declaration,
        MemberRecord method,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool linkCarrier,
        bool symbolDispatch,
        IReadOnlySet<string> symbolDestroyableDeclarationIds,
        IReadOnlyDictionary<string, DeclarationRecord> symbolByValueReturns,
        IReadOnlyDictionary<string, bool> symbolByValueCopyRequired,
        IReadOnlyDictionary<string, DeclarationRecord> symbolVectorElements,
        IReadOnlyDictionary<string, DeclarationRecord> symbolOptionalRecords,
        VariantRegistry variants,
        CallableRegistry callables,
        out AbiFunction function,
        out string? reason)
    {
        function = null!;
        reason = null;
        if (method.Signature is null)
        {
            reason = "the method signature is unavailable";
            return false;
        }

        if (method.Signature.Variadic)
        {
            reason = "variadic methods are not supported";
            return false;
        }

        if (method.Consteval)
        {
            reason = "consteval methods cannot be called at runtime";
            return false;
        }

        var returnKind = AbiKind.Void;
        var nativeReturnType = method.Signature.ReturnType.Trim();
        string? returnWrapperType = null;
        var returnOwned = false;
        var returnUniquePtr = false;
        var returnOptional = false;
        var returnArray = false;
        var returnSpan = false;
        var arrayElementKind = AbiKind.Void;
        string? arrayElementType = null;
        var arrayElementSharedWrapper = SharedWrapperKind.None;
        var arrayElementOwnership = ArrayElementOwnership.None;
        string? arrayElementWrapperType = null;
        string? arrayElementNativeType = null;
        var sharedWrapper = SharedWrapperKind.None;
        var timeConversion = TimeConversion.None;
        string? timeClockType = null;
        string? timeDurationType = null;
        var specialValue = SpecialValueKind.None;
        string? specialValueType = null;
        var mapKeyKind = AbiKind.Void;
        var mapValueKind = AbiKind.Void;
        string? mapValueWrapperType = null;
        var mapValueOwnership = ArrayElementOwnership.None;
        string? mapValueNativeType = null;
        var returnNullable = false;
        var returnExpected = false;
        string? expectedErrorType = null;
        VariantAbiInfo? returnVariant = null;
        CallableAbiInfo? returnCallable = null;
        DeclarationRecord? byValueReturn = null;
        var returnRequiresCopy = false;
        var returnTriviallyCopyable = false;
        var returnOptionalRecord = false;

        var byValueReturnName = IsPointerOrReferenceType(nativeReturnType)
            ? nativeReturnType
            : NormalizeTypeName(nativeReturnType);
        if (symbolDispatch
            && method.Kind is "method" or "operator"
            && IsByValueRecordType(byValueReturnName)
            && symbolByValueReturns.TryGetValue(byValueReturnName, out var byValueDeclaration))
        {
            byValueReturn = byValueDeclaration;
            returnRequiresCopy = symbolByValueCopyRequired.TryGetValue(byValueReturnName, out var requiresCopy)
                && requiresCopy;
            returnTriviallyCopyable = byValueDeclaration.TriviallyCopyable;
            returnKind = AbiKind.Opaque;
            returnOwned = true;
            returnWrapperType = "global::" + declarationNames[byValueDeclaration.Id].NamespaceName + "."
                + declarationNames[byValueDeclaration.Id].ClassName;
        }
        else if (symbolDispatch
            && TryParseOptional(nativeReturnType, out var optionalReturnInner)
            && TryResolveSymbolOptionalRecord(
                optionalReturnInner,
                symbolOptionalRecords,
                declarationNames,
                out var optionalReturnRecord,
                out var optionalReturnWrapper))
        {
            byValueReturn = optionalReturnRecord;
            returnKind = AbiKind.Opaque;
            returnOwned = true;
            returnOptional = true;
            returnNullable = true;
            returnTriviallyCopyable = true;
            returnOptionalRecord = true;
            returnWrapperType = optionalReturnWrapper;
            nativeReturnType = optionalReturnInner;
        }
        else if (!TryMapReturnType(
                declaration.Surface,
                method.Signature.ReturnType,
                declarationsByQualifiedName,
                declarationNames,
                destroyableDeclarationIds,
                enumUnderlyingByName,
                linkCarrier,
                symbolDispatch,
                symbolDestroyableDeclarationIds,
                symbolDispatch ? symbolVectorElements : null,
                variants,
                callables,
                out returnKind,
                out nativeReturnType,
                out returnWrapperType,
                out returnOwned,
                out returnUniquePtr,
                out returnOptional,
                out returnArray,
                out returnSpan,
                out arrayElementKind,
                out arrayElementType,
                out arrayElementSharedWrapper,
                out arrayElementOwnership,
                out arrayElementWrapperType,
                out arrayElementNativeType,
                out sharedWrapper,
                out timeConversion,
                out timeClockType,
                out timeDurationType,
                out specialValue,
                out specialValueType,
                out mapKeyKind,
                out mapValueKind,
                out mapValueWrapperType,
                out mapValueOwnership,
                out mapValueNativeType,
                out returnNullable,
                out returnExpected,
                out expectedErrorType,
                out returnVariant,
                out returnCallable,
                out var returnReason))
        {
            reason = returnReason;
            return false;
        }

        var parameters = new List<AbiParameter>();
        foreach (var parameter in method.Signature.Parameters)
        {
            if (!TryMapParameter(
                    declaration.Surface,
                    parameter.Name,
                    parameter.Type,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    enumUnderlyingByName,
                    linkCarrier || symbolDispatch,
                    symbolDispatch,
                    variants,
                    out var abiParameter,
                    out var parameterReason))
            {
                reason = parameterReason;
                return false;
            }

            parameters.Add(abiParameter);
        }

        // A mutable std::string argument is rewritten by the callee. A void method can
        // return the text in place of its result; any other return keeps its own value,
        // so the text travels through a trailing out view instead.
        var mutableStringOut = false;
        var mutableStringOutRef = false;
        var mutableStrings = parameters.Where(parameter => parameter.IsMutableString).ToList();
        if (mutableStrings.Count > 0)
        {
            if (!symbolDispatch)
            {
                reason = "mutable string references require symbol dispatch";
                return false;
            }

            if (mutableStrings.Count > 1)
            {
                reason = "multiple mutable string references are not supported";
                return false;
            }

            if (returnKind == AbiKind.Void)
            {
                returnKind = AbiKind.StringView;
                nativeReturnType = "std::basic_string<char>";
                mutableStringOut = true;
            }
            else
            {
                mutableStringOutRef = true;
            }
        }

        function = new AbiFunction
        {
            CppName = "hs_method_" + CSharpNaming.ShortHash(method.Id),
            Kind = AbiFunctionKind.Method,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            MemberId = method.Id,
            ClassCppName = declaration.QualifiedName,
            ClassName = declarationNames[declaration.Id],
            NativeMemberName = method.Name,
            CSharpMemberName = TypeNames.Sanitize(method.Name),
            ReturnKind = returnKind,
            NativeReturnType = nativeReturnType,
            ReturnWrapperType = returnWrapperType,
            ReturnOwned = returnOwned,
            ReturnUniquePtr = returnUniquePtr,
            ReturnOptional = returnOptional,
            ReturnArray = returnArray,
            ReturnSpan = returnSpan,
            ArrayElementKind = arrayElementKind,
            ArrayElementType = arrayElementType,
            ArrayElementSharedWrapper = arrayElementSharedWrapper,
            ArrayElementOwnership = arrayElementOwnership,
            ArrayElementWrapperType = arrayElementWrapperType,
            ArrayElementNativeType = arrayElementNativeType,
            SharedWrapper = sharedWrapper,
            TimeConversion = timeConversion,
            TimeClockType = timeClockType,
            TimeDurationType = timeDurationType,
            SpecialValue = specialValue,
            SpecialValueType = specialValueType,
            MapKeyKind = mapKeyKind,
            MapValueKind = mapValueKind,
            MapValueWrapperType = mapValueWrapperType,
            MapValueOwnership = mapValueOwnership,
            MapValueNativeType = mapValueNativeType,
            ReturnNullable = returnNullable,
            ReturnDeclarationId = byValueReturn?.Id,
            ReturnRequiresCopy = returnRequiresCopy,
            ReturnTriviallyCopyable = returnTriviallyCopyable,
            ReturnOptionalRecord = returnOptionalRecord,
            ReturnExpected = returnExpected,
            ExpectedErrorType = expectedErrorType,
            MutableStringOut = mutableStringOut,
            MutableStringOutRef = mutableStringOutRef,
            VariantInfo = returnVariant,
            CallableInfo = returnCallable,
            Parameters = parameters,
            Static = method.Static,
            RvalueQualified = method.RefQualifier == "rvalue",
        };
        return true;
    }

    private static bool TryMapReturnType(
        string surface,
        string canonical,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool linkCarrier,
        bool symbolDispatch,
        IReadOnlySet<string> symbolDestroyableDeclarationIds,
        IReadOnlyDictionary<string, DeclarationRecord>? symbolVectorElements,
        VariantRegistry variants,
        CallableRegistry callables,
        out AbiKind kind,
        out string nativeType,
        out string? wrapperType,
        out bool owned,
        out bool uniquePtr,
        out bool optional,
        out bool array,
        out bool returnSpan,
        out AbiKind arrayElementKind,
        out string? arrayElementType,
        out SharedWrapperKind arrayElementSharedWrapper,
        out ArrayElementOwnership arrayElementOwnership,
        out string? arrayElementWrapperType,
        out string? arrayElementNativeType,
        out SharedWrapperKind sharedWrapper,
        out TimeConversion timeConversion,
        out string? timeClockType,
        out string? timeDurationType,
        out SpecialValueKind specialValue,
        out string? specialValueType,
        out AbiKind mapKeyKind,
        out AbiKind mapValueKind,
        out string? mapValueWrapperType,
        out ArrayElementOwnership mapValueOwnership,
        out string? mapValueNativeType,
        out bool nullable,
        out bool returnExpected,
        out string? expectedErrorType,
        out VariantAbiInfo? returnVariant,
        out CallableAbiInfo? returnCallable,
        out string? reason)
    {
        kind = AbiKind.Void;
        nativeType = canonical.Trim();
        wrapperType = null;
        owned = false;
        uniquePtr = false;
        optional = false;
        array = false;
        returnSpan = false;
        arrayElementKind = AbiKind.Void;
        arrayElementType = null;
        arrayElementSharedWrapper = SharedWrapperKind.None;
        arrayElementOwnership = ArrayElementOwnership.None;
        arrayElementWrapperType = null;
        arrayElementNativeType = null;
        sharedWrapper = SharedWrapperKind.None;
        timeConversion = TimeConversion.None;
        timeClockType = null;
        timeDurationType = null;
        specialValue = SpecialValueKind.None;
        specialValueType = null;
        mapKeyKind = AbiKind.Void;
        mapValueKind = AbiKind.Void;
        mapValueWrapperType = null;
        mapValueOwnership = ArrayElementOwnership.None;
        mapValueNativeType = null;
        nullable = false;
        returnExpected = false;
        expectedErrorType = null;
        returnVariant = null;
        returnCallable = null;
        reason = null;
        if (nativeType.Length == 0)
        {
            reason = "the return type is empty";
            return false;
        }

        if (TryParseExpected(nativeType, out var expectedValueType, out var expectedError))
        {
            if (!IsStringType(expectedError) && !string.Equals(expectedError, "ll::Error", StringComparison.Ordinal))
            {
                reason = $"expected error type '{expectedError}' is not supported";
                return false;
            }

            expectedErrorType = expectedError;
            returnExpected = true;
            if (string.Equals(expectedValueType, "void", StringComparison.Ordinal))
            {
                nativeType = "void";
                return true;
            }

            if (TryParseOptional(expectedValueType, out _)
                || TryParseOptionalRef(expectedValueType, out _))
            {
                reason = $"expected value type '{expectedValueType}' is not supported";
                return false;
            }

            if (!TryMapReturnType(
                    surface,
                    expectedValueType,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    enumUnderlyingByName,
                    linkCarrier,
                    symbolDispatch,
                    symbolDestroyableDeclarationIds,
                    symbolVectorElements,
                    variants,
                    callables,
                    out kind,
                    out nativeType,
                    out wrapperType,
                    out owned,
                    out uniquePtr,
                    out optional,
                    out array,
                    out returnSpan,
                    out arrayElementKind,
                    out arrayElementType,
                    out arrayElementSharedWrapper,
                    out arrayElementOwnership,
                    out arrayElementWrapperType,
                    out arrayElementNativeType,
                    out sharedWrapper,
                    out timeConversion,
                    out timeClockType,
                    out timeDurationType,
                    out specialValue,
                    out specialValueType,
                    out mapKeyKind,
                    out mapValueKind,
                    out mapValueWrapperType,
                    out mapValueOwnership,
                    out mapValueNativeType,
                    out nullable,
                    out var nestedExpected,
                    out _,
                    out var nestedVariant,
                    out var nestedCallable,
                    out var expectedValueReason))
            {
                reason = $"expected value type '{expectedValueType}': {expectedValueReason}";
                return false;
            }

            if (nestedExpected)
            {
                reason = $"expected value type '{expectedValueType}' is not supported";
                return false;
            }

            returnVariant = nestedVariant;
            returnCallable = nestedCallable;
            return true;
        }

        if (IsStringViewType(nativeType) || IsStringType(nativeType))
        {
            kind = AbiKind.StringView;
            return true;
        }

        if (TryMapEnumUnderlying(nativeType, enumUnderlyingByName, out kind))
        {
            return true;
        }

        if (TryMapFundamental(nativeType, out kind))
        {
            return true;
        }

        var mapType = NormalizeTypeName(nativeType);
        if (!IsPointerType(nativeType) && TryParseUnorderedMap(mapType, out var mapKey, out var mapValue))
        {
            if (!TryMapMapEntryKinds(
                    surface,
                    mapKey,
                    mapValue,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    enumUnderlyingByName,
                    variants,
                    out var keyKind,
                    out var valueKind,
                    out var valueWrapper,
                    out var valueOwnership,
                    out var valueNative,
                    out var mapVariant,
                    out var mapReason))
            {
                if (IsPointerOrReferenceType(nativeType))
                {
                    // Keep reference and pointer maps whose entries cannot be projected as
                    // borrowed handles instead of dropping the member entirely.
                    kind = AbiKind.Opaque;
                    return true;
                }

                reason = mapReason;
                return false;
            }

            kind = AbiKind.MapView;
            mapKeyKind = keyKind;
            mapValueKind = valueKind;
            mapValueWrapperType = valueWrapper;
            mapValueOwnership = valueOwnership;
            mapValueNativeType = valueNative;
            returnVariant = mapVariant;
            return true;
        }

        var callableType = NormalizeTypeName(nativeType);
        if (!IsPointerType(nativeType) && callables.TryGet(surface, callableType, out var callableInfo))
        {
            kind = AbiKind.Opaque;
            nativeType = callableInfo.NativeType;
            wrapperType = callableInfo.WrapperType;
            owned = true;
            returnCallable = callableInfo;
            return true;
        }

        if (!IsPointerType(nativeType) && TryParseTimePoint(nativeType, out var timeClock, out var isSystemClock))
        {
            kind = AbiKind.I64;
            timeConversion = isSystemClock ? TimeConversion.SystemTimePoint : TimeConversion.ClockTimePoint;
            timeClockType = timeClock;
            return true;
        }

        if (!IsPointerType(nativeType) && TryParseDuration(nativeType, out var timeDuration))
        {
            kind = AbiKind.I64;
            timeConversion = TimeConversion.Duration;
            timeDurationType = timeDuration;
            return true;
        }

        if (!IsPointerType(nativeType) && TryParseTypeId(nativeType, out var typeIdCategory))
        {
            kind = AbiKind.U16;
            specialValue = SpecialValueKind.TypeId;
            specialValueType = typeIdCategory;
            return true;
        }

        if (!IsPointerType(nativeType) && IsStrongOrdering(nativeType))
        {
            kind = AbiKind.I8;
            specialValue = SpecialValueKind.StrongOrdering;
            return true;
        }

        if (!IsPointerType(nativeType) && IsFilesystemPath(nativeType))
        {
            kind = AbiKind.StringView;
            specialValue = SpecialValueKind.Path;
            return true;
        }

        var variantType = NormalizeTypeName(nativeType);
        if (!IsPointerType(nativeType) && variants.TryGet(surface, variantType, out var variantInfo))
        {
            kind = AbiKind.Opaque;
            nativeType = variantType;
            wrapperType = variantInfo.WrapperType;
            owned = true;
            returnVariant = variantInfo;
            return true;
        }

        if (IsPointerOrReferenceType(nativeType))
        {
            kind = AbiKind.Opaque;

            // A single-level pointer or lvalue reference to a public declaration becomes a
            // typed borrowed wrapper. The native side keeps ownership; the wrapper only aliases it.
            var pointerText = StripTopLevelQualifiers(nativeType);
            var isPointer = pointerText.EndsWith("*", StringComparison.Ordinal);
            var isLValueReference = pointerText.EndsWith("&", StringComparison.Ordinal)
                && !pointerText.EndsWith("&&", StringComparison.Ordinal);
            if (isPointer || isLValueReference)
            {
                var borrowedPointee = StripTopLevelQualifiers(pointerText[..^1].TrimEnd());
                if (!borrowedPointee.EndsWith("*", StringComparison.Ordinal)
                    && !borrowedPointee.EndsWith("&", StringComparison.Ordinal)
                    && declarationsByQualifiedName.TryGetValue(borrowedPointee, out var borrowedDeclaration)
                    && declarationNames.TryGetValue(borrowedDeclaration.Id, out var borrowedName))
                {
                    wrapperType = "global::" + borrowedName.NamespaceName + "." + borrowedName.ClassName;
                    nullable = isPointer;
                }
            }

            return true;
        }

        var isVector = TryParseVector(nativeType, out var sequenceElement);
        var isSet = !isVector && TryParseUnorderedSet(nativeType, out sequenceElement);
        if (isVector || isSet)
        {
            if (IsStringViewType(sequenceElement) || IsStringType(sequenceElement))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.StringView;
                arrayElementType = sequenceElement;
                array = true;
                return true;
            }

            if (TryMapEnumUnderlying(sequenceElement, enumUnderlyingByName, out var elementKind)
                || TryMapFundamental(sequenceElement, out elementKind))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = elementKind;
                arrayElementType = sequenceElement;
                array = true;
                return true;
            }

            if (IsPointerType(sequenceElement))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = sequenceElement;
                array = true;
                return true;
            }

            if (TryParseSharedPtr(sequenceElement, out var sharedElement))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Shared;
                arrayElementType = sharedElement;
                arrayElementSharedWrapper = SharedWrapperKind.SharedPtr;
                arrayElementNativeType = sharedElement;
                array = true;
                return true;
            }

            if (TryParseSharedWrapper(sequenceElement, out var sharedElementWrapper, out var wrappedElement))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Shared;
                arrayElementType = wrappedElement;
                arrayElementSharedWrapper = sharedElementWrapper;
                arrayElementNativeType = wrappedElement;
                array = true;
                return true;
            }

            if (variants.TryGet(surface, NormalizeTypeName(sequenceElement), out var sequenceVariant))
            {
                var sequenceVariantType = NormalizeTypeName(sequenceElement);
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = sequenceVariantType;
                arrayElementOwnership = ArrayElementOwnership.VariantCopy;
                arrayElementWrapperType = sequenceVariant.WrapperType;
                arrayElementNativeType = sequenceVariantType;
                returnVariant = sequenceVariant;
                array = true;
                return true;
            }

            if (TryParseUniquePtr(sequenceElement, out var uniqueElement)
                && TryResolveRecordWrapper(
                    uniqueElement,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    true,
                    out var uniqueWrapper))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = uniqueElement;
                arrayElementOwnership = ArrayElementOwnership.UniquePtr;
                arrayElementWrapperType = uniqueWrapper;
                arrayElementNativeType = uniqueElement;
                array = true;
                return true;
            }

            if (TryParseOptional(sequenceElement, out var optionalElement)
                && TryResolveRecordWrapper(
                    optionalElement,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    false,
                    out var optionalWrapper))
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = optionalElement;
                arrayElementOwnership = ArrayElementOwnership.OptionalRecordCopy;
                arrayElementWrapperType = optionalWrapper;
                arrayElementNativeType = optionalElement;
                array = true;
                return true;
            }

            // Symbol dispatch owns record elements through the shim's aligned byte copies, so
            // it does not require a compiled-in destroy function for the element type.
            var recordWrapper = TryResolveRecordWrapper(
                    sequenceElement,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    false,
                    out var resolvedRecordWrapper)
                ? resolvedRecordWrapper
                : symbolVectorElements is not null
                    && symbolVectorElements.TryGetValue(sequenceElement, out var symbolVectorElement)
                    && declarationNames.TryGetValue(symbolVectorElement.Id, out var symbolVectorElementName)
                        ? "global::" + symbolVectorElementName.NamespaceName + "." + symbolVectorElementName.ClassName
                        : null;
            if (recordWrapper is not null)
            {
                kind = AbiKind.ArrayView;
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = sequenceElement;
                arrayElementOwnership = ArrayElementOwnership.RecordCopy;
                arrayElementWrapperType = recordWrapper;
                arrayElementNativeType = sequenceElement;
                array = true;
                return true;
            }

            reason = $"{(isSet ? "set" : "vector")} return type '{sequenceElement}' is not supported";
            return false;
        }

        if (TryParseOptionalRef(nativeType, out var optionalRefInner))
        {
            kind = AbiKind.Opaque;
            nativeType = optionalRefInner;
            optional = true;
            nullable = true;

            // optional_ref is a borrowed view; use the same typed wrapper path as T*/T&.
            var optionalRefPointee = StripTopLevelQualifiers(optionalRefInner);
            if (!optionalRefPointee.EndsWith("*", StringComparison.Ordinal)
                && !optionalRefPointee.EndsWith("&", StringComparison.Ordinal)
                && declarationsByQualifiedName.TryGetValue(optionalRefPointee, out var optionalRefDeclaration)
                && declarationNames.TryGetValue(optionalRefDeclaration.Id, out var optionalRefName))
            {
                wrapperType = "global::" + optionalRefName.NamespaceName + "." + optionalRefName.ClassName;
            }

            return true;
        }

        if (TryParseOptional(nativeType, out var optionalInner))
        {
            if (IsStringViewType(optionalInner) || IsStringType(optionalInner))
            {
                kind = AbiKind.StringView;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                return true;
            }

            if (TryMapEnumUnderlying(optionalInner, enumUnderlyingByName, out kind)
                || TryMapFundamental(optionalInner, out kind))
            {
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                return true;
            }

            if (TryParseTimePoint(optionalInner, out var optionalClock, out var optionalSystemClock))
            {
                kind = AbiKind.I64;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                timeConversion = optionalSystemClock ? TimeConversion.SystemTimePoint : TimeConversion.ClockTimePoint;
                timeClockType = optionalClock;
                return true;
            }

            if (TryParseDuration(optionalInner, out var optionalDuration))
            {
                kind = AbiKind.I64;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                timeConversion = TimeConversion.Duration;
                timeDurationType = optionalDuration;
                return true;
            }

            if (TryParseTypeId(optionalInner, out var optionalTypeId))
            {
                kind = AbiKind.U16;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                specialValue = SpecialValueKind.TypeId;
                specialValueType = optionalTypeId;
                return true;
            }

            if (IsStrongOrdering(optionalInner))
            {
                kind = AbiKind.I8;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                specialValue = SpecialValueKind.StrongOrdering;
                return true;
            }

            if (IsFilesystemPath(optionalInner))
            {
                kind = AbiKind.StringView;
                nativeType = optionalInner;
                optional = true;
                nullable = true;
                specialValue = SpecialValueKind.Path;
                return true;
            }

            if (variants.TryGet(surface, NormalizeTypeName(optionalInner), out var optionalVariant))
            {
                kind = AbiKind.Opaque;
                nativeType = NormalizeTypeName(optionalInner);
                wrapperType = optionalVariant.WrapperType;
                owned = true;
                optional = true;
                nullable = true;
                returnVariant = optionalVariant;
                return true;
            }

            if (!linkCarrier || !IsByValueRecordType(optionalInner))
            {
                reason = $"optional return type '{optionalInner}' is not supported";
                return false;
            }

            if (!declarationsByQualifiedName.TryGetValue(optionalInner, out var optionalDeclaration))
            {
                reason = $"optional return type '{optionalInner}' is not a known bindable declaration";
                return false;
            }

            if (optionalDeclaration.Abstract
                || !destroyableDeclarationIds.Contains(optionalDeclaration.Id))
            {
                reason = $"optional return type '{optionalInner}' has no accessible destructor";
                return false;
            }

            if (!declarationNames.TryGetValue(optionalDeclaration.Id, out var optionalName))
            {
                reason = $"optional return type '{optionalInner}' has no generated wrapper";
                return false;
            }

            kind = AbiKind.Opaque;
            nativeType = optionalInner;
            wrapperType = "global::" + optionalName.NamespaceName + "." + optionalName.ClassName;
            owned = true;
            optional = true;
            nullable = true;
            return true;
        }

        if (TryParseSharedPtr(nativeType, out var sharedPointee))
        {
            // The shim keeps one shared reference alive in a heap holder; the managed
            // wrapper releases it through the returned release callback.
            kind = AbiKind.Shared;
            nativeType = sharedPointee;
            sharedWrapper = SharedWrapperKind.SharedPtr;
            nullable = true;
            return true;
        }

        if (TryParseWeakPtr(nativeType, out var weakPointee))
        {
            // A weak result is locked once and surfaced as a strong snapshot; expired
            // references produce a null handle.
            kind = AbiKind.Shared;
            nativeType = weakPointee;
            sharedWrapper = SharedWrapperKind.WeakPtr;
            nullable = true;
            return true;
        }

        if (symbolDispatch
            && TryParseReferenceWrapper(nativeType, out var referenceWrapper, out var referencePointee))
        {
            // OwnerPtr and StackRefResult hold a shared_ptr, and WeakRef holds a weak_ptr.
            // Bedrock::NonOwnerPointer holds a shared_ptr to a control block whose first field
            // points at the live object. The symbol shim uses the shared/weak return ABI and
            // resolves the non-owner target from that control block.
            kind = AbiKind.Shared;
            nativeType = referencePointee;
            sharedWrapper = referenceWrapper;
            nullable = true;
            return true;
        }

        if (TryParseSharedWrapper(nativeType, out var sharedWrapperKind, out var wrappedPointee))
        {
            kind = AbiKind.Shared;
            nativeType = wrappedPointee;
            sharedWrapper = sharedWrapperKind;
            nullable = sharedWrapperKind == SharedWrapperKind.Nullable;
            return true;
        }

        if (TryParsePathBuffer(nativeType, out var pathBufferType))
        {
            // Core::PathBuffer<T> stores exactly one T value. For std::string the layout and the
            // non-trivial return ABI match std::string, so the shim can read the string directly.
            kind = AbiKind.StringView;
            nativeType = pathBufferType;
            return true;
        }

        if (TryParseUniquePtr(nativeType, out var uniquePointee))
        {
            if (!declarationsByQualifiedName.TryGetValue(uniquePointee, out var pointeeDeclaration))
            {
                reason = $"unique_ptr return type '{uniquePointee}' is not a known bindable declaration";
                return false;
            }

            if (!destroyableDeclarationIds.Contains(pointeeDeclaration.Id)
                && !(symbolDispatch && symbolDestroyableDeclarationIds.Contains(pointeeDeclaration.Id)))
            {
                reason = $"unique_ptr return type '{uniquePointee}' has no accessible destructor";
                return false;
            }

            if (!declarationNames.TryGetValue(pointeeDeclaration.Id, out var uniqueName))
            {
                reason = $"unique_ptr return type '{uniquePointee}' has no generated wrapper";
                return false;
            }

            kind = AbiKind.Opaque;
            nativeType = uniquePointee;
            wrapperType = "global::" + uniqueName.NamespaceName + "." + uniqueName.ClassName;
            owned = true;
            uniquePtr = true;
            nullable = true;
            return true;
        }

        if (symbolDispatch && TryParseSpan(nativeType, out var spanElement))
        {
            var spanElementText = spanElement.Trim();
            if (spanElementText.StartsWith("gsl::not_null<", StringComparison.Ordinal)
                && spanElementText.EndsWith(">", StringComparison.Ordinal))
            {
                var inner = spanElementText["gsl::not_null<".Length..^1].Trim();
                if (IsPointerType(inner))
                {
                    spanElementText = inner;
                }
            }

            arrayElementOwnership = ArrayElementOwnership.None;
            if (IsPointerType(spanElementText))
            {
                // Pointer elements surface as nint[] without ownership.
                arrayElementKind = AbiKind.Opaque;
                arrayElementNativeType = spanElementText;
            }
            else if (TryMapEnumUnderlying(spanElementText, enumUnderlyingByName, out var spanScalar)
                || TryMapFundamental(spanElementText, out spanScalar))
            {
                // Scalars are copied into a managed array right after the call.
                arrayElementKind = spanScalar;
                arrayElementNativeType = spanElementText;
            }
            else if ((TryResolveRecordWrapper(
                        spanElementText,
                        declarationsByQualifiedName,
                        declarationNames,
                        destroyableDeclarationIds,
                        false,
                        out var resolvedSpanWrapper)
                    ? resolvedSpanWrapper
                    : symbolVectorElements is not null
                        && symbolVectorElements.TryGetValue(StripTopLevelQualifiers(spanElementText), out var spanElementRecord)
                        && declarationNames.TryGetValue(spanElementRecord.Id, out var spanElementName)
                            ? "global::" + spanElementName.NamespaceName + "." + spanElementName.ClassName
                            : null) is { } spanRecordWrapper)
            {
                // A borrowed span of records is copied element by element so each wrapper
                // owns its storage.
                arrayElementKind = AbiKind.Opaque;
                arrayElementType = StripTopLevelQualifiers(spanElementText);
                arrayElementOwnership = ArrayElementOwnership.RecordCopy;
                arrayElementWrapperType = spanRecordWrapper;
                arrayElementNativeType = StripTopLevelQualifiers(spanElementText);
            }
            else
            {
                reason = $"span element type '{spanElementText}' is not supported";
                return false;
            }

            // gsl::span<T> is a 16-byte {size, pointer} view. The shim converts it to the
            // pointer-first C ABI view.
            kind = AbiKind.ArrayView;
            nativeType = canonical.Trim();
            array = true;
            returnSpan = true;
            return true;
        }

        if (!IsByValueRecordType(nativeType))
        {
            reason = $"return type '{canonical}' is not supported";
            return false;
        }

        if (!linkCarrier)
        {
            reason = $"by-value return type '{canonical}' requires --link-carrier or --virtual-dispatch";
            return false;
        }

        // A by-value record result is copied into a shim-owned object. The copy can only be
        // released through the class emitted destroy function, so require an accessible one.
        var byValueName = StripTopLevelQualifiers(nativeType);
        if (!declarationsByQualifiedName.TryGetValue(byValueName, out var resultDeclaration))
        {
            reason = $"by-value return type '{byValueName}' is not a known bindable declaration";
            return false;
        }

        if (resultDeclaration.Abstract)
        {
            reason = $"by-value return type '{byValueName}' is abstract";
            return false;
        }

        if (!destroyableDeclarationIds.Contains(resultDeclaration.Id))
        {
            reason = $"by-value return type '{byValueName}' has no accessible destructor";
            return false;
        }

        if (!declarationNames.TryGetValue(resultDeclaration.Id, out var resultName))
        {
            reason = $"by-value return type '{byValueName}' has no generated wrapper";
            return false;
        }

        kind = AbiKind.Opaque;
        nativeType = byValueName;
        wrapperType = "global::" + resultName.NamespaceName + "." + resultName.ClassName;
        owned = true;
        return true;
    }

    private static bool TryParseVector(string canonical, out string element)
    {
        element = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::vector<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        // The symbol frontend spells the default allocator explicitly; the header frontend
        // omits it. A custom allocator is a different type and must not be reinterpreted.
        if (arguments.Count is not (1 or 2)
            || (arguments.Count == 2
                && !arguments[1].Replace(" ", "", StringComparison.Ordinal)
                    .StartsWith("std::allocator<", StringComparison.Ordinal)))
        {
            return false;
        }

        element = StripTopLevelQualifiers(arguments[0].Trim());
        return element.Length > 0;
    }

    private static bool TryParseVariant(string canonical, out List<string> alternatives)
    {
        alternatives = [];
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::variant<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var argument in SplitTemplateArguments(text[prefix.Length..^1].Trim()))
        {
            var alternative = StripTopLevelQualifiers(argument.Trim());
            if (alternative.Length == 0)
            {
                return false;
            }

            alternatives.Add(alternative);
        }

        return alternatives.Count > 0;
    }

    private static string VariantScalarName(AbiKind kind) => kind switch
    {
        AbiKind.Bool => "Bool",
        AbiKind.I8 => "SByte",
        AbiKind.U8 => "Byte",
        AbiKind.I16 => "Int16",
        AbiKind.U16 => "UInt16",
        AbiKind.I32 => "Int32",
        AbiKind.U32 => "UInt32",
        AbiKind.I64 => "Int64",
        AbiKind.U64 => "UInt64",
        AbiKind.USize => "NUInt",
        AbiKind.ISize => "NInt",
        AbiKind.F32 => "Single",
        AbiKind.F64 => "Double",
        _ => "Value",
    };

    private static string VariantPointerName(string nativeType)
    {
        var text = nativeType.TrimEnd();
        while (text.EndsWith("*", StringComparison.Ordinal))
        {
            text = text[..^1].TrimEnd();
        }

        return TypeNames.Sanitize(TypeNames.NativeName(text)) + "Pointer";
    }

    private static CallableRegistry BuildCallableRegistry(
        IReadOnlyList<IrRecord> records,
        IReadOnlyDictionary<string, string> enumUnderlyingByName)
    {
        var surfaceByDeclaration = records.OfType<DeclarationRecord>()
            .ToDictionary(declaration => declaration.Id, declaration => declaration.Surface, StringComparer.Ordinal);
        var registry = new CallableRegistry();
        foreach (var member in records.OfType<MemberRecord>())
        {
            if (member.Signature is null
                || IsPointerOrReferenceType(member.Signature.ReturnType)
                || !surfaceByDeclaration.TryGetValue(member.DeclarationId, out var surface))
            {
                continue;
            }

            var callableType = NormalizeTypeName(member.Signature.ReturnType);
            if (!callableType.StartsWith("std::function<", StringComparison.Ordinal)
                || registry.TryGet(surface, callableType, out _))
            {
                continue;
            }

            if (!TryParseStdFunction(callableType, enumUnderlyingByName, out var signature, out _))
            {
                continue;
            }

            registry.Add(new CallableAbiInfo
            {
                NativeType = callableType,
                Surface = surface,
                ClassName = "Function_" + CSharpNaming.ShortHash(callableType),
                Signature = signature,
            });
        }

        return registry;
    }

    private static IEnumerable<AbiFunction> BuildCallableFunctions(CallableAbiInfo info)
    {
        var hash = CSharpNaming.ShortHash(info.NativeType);
        var declarationId = "callable:" + info.NativeType;
        var className = new DeclarationName(info.NamespaceName, "", info.ClassName);
        var parameters = new List<AbiParameter>();
        for (var index = 0; index < info.Signature.Arguments.Count; index++)
        {
            parameters.Add(ToCallableParameter(info.Signature.Arguments[index], index));
        }

        var invoke = new AbiFunction
        {
            CppName = $"hs_callable_{hash}_invoke",
            Kind = AbiFunctionKind.Method,
            Surface = info.Surface,
            DeclarationId = declarationId,
            MemberId = declarationId + ":invoke",
            ClassCppName = info.NativeType,
            ClassName = className,
            NativeMemberName = "operator()",
            CSharpMemberName = "Invoke",
            ReturnKind = info.Signature.ReturnKind,
            NativeReturnType = info.Signature.ReturnNativeType,
            Parameters = parameters,
            CallableInfo = info,
            CallableOp = CallableOp.Invoke,
        };
        var destroy = new AbiFunction
        {
            CppName = $"hs_callable_{hash}_destroy",
            Kind = AbiFunctionKind.Destroy,
            Surface = info.Surface,
            DeclarationId = declarationId,
            MemberId = declarationId + ":destroy",
            ClassCppName = info.NativeType,
            ClassName = className,
            NativeMemberName = "~function",
            CSharpMemberName = "Dispose",
            ReturnKind = AbiKind.Void,
            Parameters = [],
            CallableInfo = info,
        };
        info.InvokeFunction = invoke;
        info.DestroyFunction = destroy;
        return [invoke, destroy];
    }

    private static AbiParameter ToCallableParameter(AbiCallbackArgument argument, int index)
    {
        var name = "arg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (argument.Kind != AbiKind.Opaque)
        {
            return new AbiParameter { Name = name, Kind = argument.Kind, NativeType = argument.NativeType };
        }

        if (!argument.IsPointer
            && !argument.IsReference
            && IsByValueRecordType(argument.NativeType))
        {
            var recordType = NormalizeTypeName(argument.NativeType);
            return new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = argument.NativeType,
                NativePointee = recordType,
                IsReference = true,
                IsConst = true,
            };
        }

        return new AbiParameter
        {
            Name = name,
            Kind = AbiKind.Opaque,
            NativeType = argument.NativeType,
            NativePointee = ExtractPointee(argument.NativeType),
            IsReference = argument.IsReference,
            IsConst = argument.IsConst,
        };
    }

    private static HashSet<string> CollectMemberlessVariantAlternatives(
        IReadOnlyList<IrRecord> records,
        IReadOnlyDictionary<string, List<MemberRecord>> membersByDeclaration,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in records.OfType<MemberRecord>())
        {
            if (member.Signature is not null)
            {
                Consider(member.Signature.ReturnType);
                foreach (var parameter in member.Signature.Parameters)
                {
                    Consider(parameter.Type);
                }
            }

            if (member.Type is not null)
            {
                Consider(member.Type.Canonical);
            }
        }

        return result;

        void Consider(string type)
        {
            var normalized = NormalizeTypeName(type);
            if (TryParseVariant(normalized, out var alternatives))
            {
                foreach (var alternative in alternatives)
                {
                    if (!declarationsByQualifiedName.TryGetValue(alternative, out var declaration)
                        || declaration.Access != "public"
                        || declaration.Abstract
                        || !declaration.CopyConstructible
                        || membersByDeclaration.ContainsKey(declaration.Id))
                    {
                        continue;
                    }

                    result.Add(declaration.Id);
                }

                return;
            }

            if (TryParseUnorderedMap(normalized, out var mapKey, out var mapValue))
            {
                Consider(mapKey);
                Consider(mapValue);
                return;
            }

            if (TryParseOptional(normalized, out var inner)
                || TryParseOptionalRef(normalized, out inner)
                || TryParseExpected(normalized, out inner, out _)
                || TryParseVector(normalized, out inner))
            {
                Consider(inner);
            }
        }
    }

    private static VariantRegistry BuildVariantRegistry(
        IReadOnlyList<IrRecord> records,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool allowOwnedByValue)
    {
        var surfaceByDeclaration = records.OfType<DeclarationRecord>()
            .ToDictionary(declaration => declaration.Id, declaration => declaration.Surface, StringComparer.Ordinal);
        var registry = new VariantRegistry();
        foreach (var member in records.OfType<MemberRecord>())
        {
            if (!surfaceByDeclaration.TryGetValue(member.DeclarationId, out var surface))
            {
                continue;
            }

            if (member.Signature is not null)
            {
                Consider(surface, member.Signature.ReturnType);
                foreach (var parameter in member.Signature.Parameters)
                {
                    Consider(surface, parameter.Type);
                }
            }

            if (member.Type is not null)
            {
                Consider(surface, member.Type.Canonical);
            }
        }

        return registry;

        void Consider(string surface, string type)
        {
            var normalized = NormalizeTypeName(type);
            if (TryParseVariant(normalized, out _))
            {
                Register(surface, normalized);
                return;
            }

            if (TryParseUnorderedMap(normalized, out var mapKey, out var mapValue))
            {
                Consider(surface, mapKey);
                Consider(surface, mapValue);
                return;
            }

            if (TryParseOptional(normalized, out var inner)
                || TryParseOptionalRef(normalized, out inner)
                || TryParseExpected(normalized, out inner, out _)
                || TryParseVector(normalized, out inner))
            {
                Consider(surface, inner);
            }
        }

        void Register(string surface, string nativeType)
        {
            if (registry.TryGet(surface, nativeType, out _)
                || !TryParseVariant(nativeType, out var alternativeTypes))
            {
                return;
            }

            var alternatives = new List<VariantAlternative>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var usedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alternativeType in alternativeTypes)
            {
                if (!usedTypes.Add(alternativeType))
                {
                    return;
                }

                VariantAlternativeKind kind;
                AbiKind scalarKind = AbiKind.Void;
                string? wrapperType = null;
                string baseName;
                if (IsStringType(alternativeType))
                {
                    kind = VariantAlternativeKind.String;
                    baseName = "String";
                }
                else if (TryMapEnumUnderlying(alternativeType, enumUnderlyingByName, out scalarKind)
                    || TryMapFundamental(alternativeType, out scalarKind))
                {
                    kind = VariantAlternativeKind.Scalar;
                    baseName = VariantScalarName(scalarKind);
                }
                else if (IsPointerType(alternativeType)
                    && !alternativeType.Contains('(', StringComparison.Ordinal))
                {
                    kind = VariantAlternativeKind.Pointer;
                    baseName = VariantPointerName(alternativeType);
                }
                else if (allowOwnedByValue
                    && declarationsByQualifiedName.TryGetValue(alternativeType, out var declaration)
                    && declaration.Access == "public"
                    && !declaration.Abstract
                    && declaration.CopyConstructible
                    && destroyableDeclarationIds.Contains(declaration.Id)
                    && declarationNames.TryGetValue(declaration.Id, out var alternativeName))
                {
                    kind = VariantAlternativeKind.Record;
                    wrapperType = "global::" + alternativeName.NamespaceName + "." + alternativeName.ClassName;
                    baseName = TypeNames.Sanitize(TypeNames.NativeName(alternativeType));
                }
                else
                {
                    return;
                }

                var managedName = baseName;
                var suffix = 2;
                while (!usedNames.Add(managedName))
                {
                    managedName = baseName + "_" + suffix;
                    suffix++;
                }

                alternatives.Add(new VariantAlternative
                {
                    Index = alternatives.Count,
                    NativeType = alternativeType,
                    Kind = kind,
                    ScalarKind = scalarKind,
                    WrapperType = wrapperType,
                    ManagedName = managedName,
                });
            }

            registry.Add(new VariantAbiInfo
            {
                NativeType = nativeType,
                Surface = surface,
                ClassName = "Variant_" + CSharpNaming.ShortHash(nativeType),
                Alternatives = alternatives,
            });
        }
    }

    private static IEnumerable<AbiFunction> BuildVariantFunctions(VariantAbiInfo info)
    {
        var functions = new List<AbiFunction>();
        var hash = CSharpNaming.ShortHash(info.NativeType);
        var className = new DeclarationName(info.NamespaceName, "", info.ClassName);
        var declarationId = "variant:" + info.NativeType;

        foreach (var alternative in info.Alternatives)
        {
            var parameters = new List<AbiParameter>();
            switch (alternative.Kind)
            {
                case VariantAlternativeKind.String:
                    parameters.Add(new AbiParameter
                    {
                        Name = "value",
                        Kind = AbiKind.StringView,
                        NativeType = alternative.NativeType,
                    });
                    break;
                case VariantAlternativeKind.Record:
                    parameters.Add(new AbiParameter
                    {
                        Name = "value",
                        Kind = AbiKind.Opaque,
                        NativeType = alternative.NativeType,
                        NativePointee = alternative.NativeType,
                        IsReference = true,
                        IsConst = true,
                        WrapperType = alternative.WrapperType,
                    });
                    break;
                case VariantAlternativeKind.Pointer:
                    parameters.Add(new AbiParameter
                    {
                        Name = "value",
                        Kind = AbiKind.Opaque,
                        NativeType = alternative.NativeType,
                        NativePointee = alternative.NativeType,
                    });
                    break;
                case VariantAlternativeKind.Scalar:
                    parameters.Add(new AbiParameter
                    {
                        Name = "value",
                        Kind = alternative.ScalarKind,
                        NativeType = alternative.NativeType,
                    });
                    break;
            }

            var create = new AbiFunction
            {
                CppName = $"hs_variant_{hash}_create_{alternative.Index}",
                Kind = AbiFunctionKind.Create,
                Surface = info.Surface,
                DeclarationId = declarationId,
                MemberId = $"{declarationId}:create:{alternative.Index}",
                ClassCppName = info.NativeType,
                ClassName = className,
                NativeMemberName = "create",
                CSharpMemberName = "Create" + alternative.ManagedName,
                ReturnKind = AbiKind.Opaque,
                NativeReturnType = info.NativeType,
                Parameters = parameters,
                Static = true,
                VariantInfo = info,
                VariantOp = VariantOp.Create,
                VariantAlternative = alternative,
            };
            info.CreateFunctions.Add(create);
            functions.Add(create);

            var getKind = alternative.Kind switch
            {
                VariantAlternativeKind.String => AbiKind.StringView,
                VariantAlternativeKind.Scalar => alternative.ScalarKind,
                _ => AbiKind.Opaque,
            };
            var get = new AbiFunction
            {
                CppName = $"hs_variant_{hash}_get_{alternative.Index}",
                Kind = AbiFunctionKind.Method,
                Surface = info.Surface,
                DeclarationId = declarationId,
                MemberId = $"{declarationId}:get:{alternative.Index}",
                ClassCppName = info.NativeType,
                ClassName = className,
                NativeMemberName = "get",
                CSharpMemberName = "As" + alternative.ManagedName,
                ReturnKind = getKind,
                NativeReturnType = alternative.NativeType,
                Parameters = [],
                VariantInfo = info,
                VariantOp = VariantOp.Get,
                VariantAlternative = alternative,
            };
            info.GetFunctions.Add(get);
            functions.Add(get);
        }

        var index = new AbiFunction
        {
            CppName = $"hs_variant_{hash}_index",
            Kind = AbiFunctionKind.Method,
            Surface = info.Surface,
            DeclarationId = declarationId,
            MemberId = declarationId + ":index",
            ClassCppName = info.NativeType,
            ClassName = className,
            NativeMemberName = "index",
            CSharpMemberName = "Index",
            ReturnKind = AbiKind.I32,
            NativeReturnType = "int",
            Parameters = [],
            VariantInfo = info,
            VariantOp = VariantOp.Index,
        };
        info.IndexFunction = index;
        functions.Add(index);

        var destroy = new AbiFunction
        {
            CppName = $"hs_variant_{hash}_destroy",
            Kind = AbiFunctionKind.Destroy,
            Surface = info.Surface,
            DeclarationId = declarationId,
            MemberId = declarationId + ":destroy",
            ClassCppName = info.NativeType,
            ClassName = className,
            NativeMemberName = "~variant",
            CSharpMemberName = "Dispose",
            ReturnKind = AbiKind.Void,
            Parameters = [],
            VariantInfo = info,
        };
        info.DestroyFunction = destroy;
        functions.Add(destroy);
        return functions;
    }

    private static bool TryParseExpected(string canonical, out string valueType, out string errorType)
    {
        valueType = "";
        errorType = "";
        var text = NormalizeTypeName(canonical);
        var prefix = text.StartsWith("nonstd::expected_lite::expected<", StringComparison.Ordinal)
            ? "nonstd::expected_lite::expected<"
            : text.StartsWith("nonstd::expected<", StringComparison.Ordinal)
                ? "nonstd::expected<"
                : null;
        if (prefix is null || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 2)
        {
            return false;
        }

        valueType = StripTopLevelQualifiers(arguments[0].Trim());
        errorType = StripTopLevelQualifiers(arguments[1].Trim());
        return valueType.Length > 0 && errorType.Length > 0;
    }

    private static bool TryParseOptionalRef(string canonical, out string inner)
    {
        inner = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "optional_ref<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        inner = StripTopLevelQualifiers(arguments[0].Trim());
        return inner.Length > 0;
    }

    private static bool TryParseOptional(string canonical, out string inner)
    {
        inner = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::optional<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        inner = StripTopLevelQualifiers(arguments[0].Trim());
        return inner.Length > 0;
    }

    private static bool TryParseTimePoint(string canonical, out string clockType, out bool isSystemClock)
    {
        clockType = "";
        isSystemClock = false;
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::chrono::time_point<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count is < 1 or > 2)
        {
            return false;
        }

        clockType = StripTopLevelQualifiers(arguments[0].Trim());
        isSystemClock = string.Equals(clockType, "std::chrono::system_clock", StringComparison.Ordinal);
        return clockType.Length > 0;
    }

    private static bool TryParseDuration(string canonical, out string durationType)
    {
        durationType = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::chrono::duration<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        durationType = text;
        return true;
    }

    private static bool TryParseTypeId(string canonical, out string category)
    {
        category = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "Bedrock::typeid_t<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        category = arguments[0].Trim();
        return category.Length > 0;
    }

    private static bool IsStrongOrdering(string canonical) =>
        string.Equals(NormalizeTypeName(canonical), "std::strong_ordering", StringComparison.Ordinal);

    private static bool IsFilesystemPath(string canonical) =>
        string.Equals(NormalizeTypeName(canonical), "std::filesystem::path", StringComparison.Ordinal);

    private static string CSharpMapValueTypeKind(AbiKind kind) =>
        kind == AbiKind.StringView ? "string" : CSharpMapKeyType(kind);

    private static bool TryMapPairElementKinds(
        string first,
        string second,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiKind keyKind,
        out AbiKind valueKind)
    {
        keyKind = AbiKind.Void;
        valueKind = AbiKind.Void;
        return TryMapMapScalarKind(first, enumUnderlyingByName, out keyKind)
            && TryMapMapScalarKind(second, enumUnderlyingByName, out valueKind);
    }

    private static bool TryMapMapScalarKind(
        string type,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiKind kind)
    {
        if (IsStringViewType(type) || IsStringType(type))
        {
            kind = AbiKind.StringView;
            return true;
        }

        if (TryMapEnumUnderlying(type, enumUnderlyingByName, out kind)
            || TryMapFundamental(type, out kind))
        {
            return true;
        }

        if (IsPointerType(type))
        {
            kind = AbiKind.Opaque;
            return true;
        }

        kind = AbiKind.Void;
        return false;
    }

    private static bool TryParsePair(string canonical, out string first, out string second)
    {
        first = "";
        second = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::pair<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 2)
        {
            return false;
        }

        first = StripTopLevelQualifiers(arguments[0].Trim());
        second = StripTopLevelQualifiers(arguments[1].Trim());
        return first.Length > 0 && second.Length > 0;
    }

    private static bool TryMapMapEntryKinds(
        string surface,
        string mapKey,
        string mapValue,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        VariantRegistry variants,
        out AbiKind keyKind,
        out AbiKind valueKind,
        out string? valueWrapper,
        out ArrayElementOwnership valueOwnership,
        out string? valueNative,
        out VariantAbiInfo? valueVariant,
        out string? reason)
    {
        keyKind = AbiKind.Void;
        valueKind = AbiKind.Void;
        valueWrapper = null;
        valueOwnership = ArrayElementOwnership.None;
        valueNative = null;
        valueVariant = null;
        reason = null;

        if (IsStringViewType(mapKey) || IsStringType(mapKey))
        {
            keyKind = AbiKind.StringView;
        }
        else if (!TryMapEnumUnderlying(mapKey, enumUnderlyingByName, out keyKind)
            && !TryMapFundamental(mapKey, out keyKind))
        {
            if (!IsPointerType(mapKey))
            {
                reason = $"map key type '{mapKey}' is not supported";
                return false;
            }

            keyKind = AbiKind.Opaque;
        }

        if (IsStringViewType(mapValue) || IsStringType(mapValue))
        {
            valueKind = AbiKind.StringView;
        }
        else if (TryMapEnumUnderlying(mapValue, enumUnderlyingByName, out valueKind)
            || TryMapFundamental(mapValue, out valueKind))
        {
        }
        else if (IsPointerType(mapValue))
        {
            valueKind = AbiKind.Opaque;
        }
        else if (variants.TryGet(surface, NormalizeTypeName(mapValue), out var mapValueVariant))
        {
            valueKind = AbiKind.Opaque;
            valueOwnership = ArrayElementOwnership.VariantCopy;
            valueWrapper = mapValueVariant.WrapperType;
            valueNative = NormalizeTypeName(mapValue);
            valueVariant = mapValueVariant;
        }
        else if (TryResolveRecordWrapper(
                mapValue,
                declarationsByQualifiedName,
                declarationNames,
                destroyableDeclarationIds,
                false,
                out var mapRecordWrapper))
        {
            valueKind = AbiKind.Opaque;
            valueOwnership = ArrayElementOwnership.RecordCopy;
            valueWrapper = mapRecordWrapper;
            valueNative = mapValue;
        }
        else
        {
            reason = $"map value type '{mapValue}' is not supported";
            return false;
        }

        return true;
    }

    private static bool TryParseUnorderedMap(string canonical, out string key, out string value)
    {
        key = "";
        value = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::unordered_map<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 2)
        {
            return false;
        }

        key = StripTopLevelQualifiers(arguments[0].Trim());
        value = StripTopLevelQualifiers(arguments[1].Trim());
        return key.Length > 0 && value.Length > 0;
    }

    private static bool TryParseUnorderedSet(string canonical, out string element)
    {
        element = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::unordered_set<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        element = StripTopLevelQualifiers(arguments[0].Trim());
        return element.Length > 0;
    }

    private static bool TryParseReferenceWrapper(
        string canonical,
        out SharedWrapperKind wrapper,
        out string pointee)
    {
        wrapper = SharedWrapperKind.None;
        pointee = "";
        var text = NormalizeTypeName(canonical);
        if (text.StartsWith("gsl::not_null<", StringComparison.Ordinal)
            && text.EndsWith(">", StringComparison.Ordinal))
        {
            return TryParseReferenceWrapper(text["gsl::not_null<".Length..^1].Trim(), out wrapper, out pointee);
        }

        (string Prefix, SharedWrapperKind Kind)[] prefixes =
        [
            ("OwnerPtr<", SharedWrapperKind.SharedPtr),
            ("StackRefResult<", SharedWrapperKind.SharedPtr),
            ("WeakRef<", SharedWrapperKind.WeakPtr),
            ("Bedrock::NonOwnerPointer<", SharedWrapperKind.NonOwnerPtr),
            ("NonOwnerPointer<", SharedWrapperKind.NonOwnerPtr),
        ];
        foreach (var (prefix, kind) in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)
                || !text.EndsWith(">", StringComparison.Ordinal))
            {
                continue;
            }

            var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
            if (arguments.Count != 1)
            {
                return false;
            }

            pointee = StripTopLevelQualifiers(arguments[0].Trim());
            if (pointee.Length == 0)
            {
                return false;
            }

            wrapper = kind;
            return true;
        }

        return false;
    }

    private static bool TryParseSharedWrapper(
        string canonical,
        out SharedWrapperKind wrapper,
        out string pointee)
    {
        wrapper = SharedWrapperKind.None;
        pointee = "";
        var text = NormalizeTypeName(canonical);
        string prefix;
        if (text.StartsWith("endstone::NotNull<", StringComparison.Ordinal))
        {
            prefix = "endstone::NotNull<";
            wrapper = SharedWrapperKind.NotNull;
        }
        else if (text.StartsWith("endstone::Nullable<", StringComparison.Ordinal))
        {
            prefix = "endstone::Nullable<";
            wrapper = SharedWrapperKind.Nullable;
        }
        else
        {
            return false;
        }

        if (!text.EndsWith(">", StringComparison.Ordinal))
        {
            wrapper = SharedWrapperKind.None;
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            wrapper = SharedWrapperKind.None;
            return false;
        }

        pointee = StripTopLevelQualifiers(arguments[0].Trim());
        return pointee.Length > 0;
    }

    private static bool TryParseSharedPtr(string canonical, out string pointee)
    {
        pointee = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::shared_ptr<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        pointee = StripTopLevelQualifiers(arguments[0].Trim());
        return pointee.Length > 0;
    }

    /// <summary>
    /// Recognizes <c>Core::PathBuffer&lt;std::string&gt;</c>. The wrapper has a single string
    /// member and no other state, so its size, alignment, and non-trivial return ABI equal
    /// <c>std::string</c>.
    /// </summary>
    private static bool TryParsePathBuffer(string canonical, out string stringType)
    {
        stringType = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "Core::PathBuffer<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal)
            || IsPointerOrReferenceType(canonical))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1 || !IsStringType(arguments[0]))
        {
            return false;
        }

        stringType = "std::basic_string<char>";
        return true;
    }

    private static bool TryParseWeakPtr(string canonical, out string pointee)
    {
        pointee = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::weak_ptr<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count != 1)
        {
            return false;
        }

        pointee = StripTopLevelQualifiers(arguments[0].Trim());
        return pointee.Length > 0;
    }

    private static bool TryParseSpan(string canonical, out string element)
    {
        element = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "gsl::span<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1].Trim()).ToList();
        if (arguments.Count is < 1 or > 2)
        {
            return false;
        }

        if (arguments.Count == 2 && !string.Equals(arguments[1].Trim(), "-1", StringComparison.Ordinal))
        {
            return false;
        }

        element = arguments[0].Trim();
        if (element.StartsWith("gsl::not_null<", StringComparison.Ordinal)
            && element.EndsWith(">", StringComparison.Ordinal))
        {
            element = element["gsl::not_null<".Length..^1].Trim();
        }

        return element.Length > 0;
    }

    private static bool TryParseBitset(string canonical, out int bits)
    {
        bits = 0;
        var text = canonical.Trim();
        const string prefix = "std::bitset<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(text[prefix.Length..^1].Trim(), out bits)
            && bits > 0
            && bits <= 64;
    }

    private static bool TryParseUniquePtr(string canonical, out string pointee)
    {
        pointee = "";
        var text = NormalizeTypeName(canonical);
        const string prefix = "std::unique_ptr<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = text[prefix.Length..^1].Trim();
        var arguments = SplitTemplateArguments(inner).ToList();
        if (arguments.Count == 2)
        {
            // The symbol stream spells the default deleter explicitly.
            var expected = "std::default_delete<" + arguments[0].Trim() + ">";
            if (!string.Equals(
                    CompactTemplateName(NormalizeTypeName(arguments[1].Trim())),
                    CompactTemplateName(expected),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (arguments.Count != 1)
        {
            // A custom deleter owns release semantics the shim cannot reproduce.
            return false;
        }

        pointee = StripTopLevelQualifiers(arguments[0].Trim());
        return pointee.Length > 0;
    }

    private static bool TryBuildField(
        DeclarationRecord declaration,
        MemberRecord field,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool linkCarrier,
        VariantRegistry variants,
        out AbiFunction getter,
        out AbiFunction? setter,
        out string? reason)
    {
        getter = null!;
        setter = null;
        reason = null;
        var type = field.Type!;
        var text = type.Canonical.Trim();
        var kind = AbiKind.Void;
        var bitsetBits = 0;
        if (IsStringViewType(text) || IsStringType(text))
        {
            kind = AbiKind.StringView;
        }
        else if (TryParseBitset(StripTopLevelQualifiers(text).TrimEnd().TrimEnd('&', ' '), out bitsetBits))
        {
            // std::bitset is bit-packed; the member is aliased as the integer its storage
            // holds, matching what the shim reads and writes.
            kind = bitsetBits <= 8 ? AbiKind.U8
                : bitsetBits <= 16 ? AbiKind.U16
                : bitsetBits <= 32 ? AbiKind.U32
                : AbiKind.U64;
        }
        else if (!TryMapEnumUnderlying(text, enumUnderlyingByName, out kind)
            && !TryMapFundamental(text, out kind))
        {
            if (!TryMapOpaque(text, declarationsByQualifiedName, declarationNames, out var pointee, out var wrapperType))
            {
                if (!IsByValueRecordType(text) && !IsArrayType(text))
                {
                    reason = $"field type '{text}' is not supported";
                    return false;
                }

                pointee = StripTopLevelQualifiers(text);
                wrapperType = LookupWrapperType(text, declarationsByQualifiedName, declarationNames);
            }

            kind = AbiKind.Opaque;
            _ = pointee;
            _ = wrapperType;
        }

        var name = string.IsNullOrWhiteSpace(field.Name) ? "value" : field.Name;
        getter = new AbiFunction
        {
            CppName = "hs_get_" + CSharpNaming.ShortHash(field.Id),
            Kind = AbiFunctionKind.Get,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            MemberId = field.Id,
            ClassCppName = declaration.QualifiedName,
            ClassName = declarationNames[declaration.Id],
            NativeMemberName = field.Name,
            CSharpMemberName = TypeNames.Sanitize(name),
            ReturnKind = kind,
            NativeReturnType = text,
            Static = field.Static,
            IsBitset = bitsetBits > 0,
            BitsetBits = bitsetBits,
        };

        if (kind is AbiKind.StringView
            || type.Const
            || IsConstType(text)
            || IsArrayType(text)
            || type.Kind == "reference"
            || IsReferenceType(text))
        {
            // A reference field cannot be assigned through the wrapper.
            return true;
        }

        if (!TryMapParameter(
                declaration.Surface,
                name,
                text,
                declarationsByQualifiedName,
                declarationNames,
                destroyableDeclarationIds,
                enumUnderlyingByName,
                linkCarrier,
                symbolDispatch: false,
                variants,
                out var valueParameter,
                out _))
        {
            return true;
        }

        if (valueParameter.Kind == AbiKind.Callback
            || valueParameter.Kind == AbiKind.Shared
            || valueParameter.IsOptional
            || valueParameter.IsVector
            || valueParameter.IsMap)
        {
            // Callback and optional fields are readable as opaque views; a setter would need
            // the hidden presence flag to be threaded through the field ABI.
            return true;
        }

        if (!linkCarrier
            && !IsPointerType(text)
            && !IsReferenceType(text)
            && IsByValueRecordType(text))
        {
            // Copy assignment of a record field may call out-of-line native members.
            return true;
        }

        setter = new AbiFunction
        {
            CppName = "hs_set_" + CSharpNaming.ShortHash(field.Id),
            Kind = AbiFunctionKind.Set,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            MemberId = field.Id,
            ClassCppName = declaration.QualifiedName,
            ClassName = declarationNames[declaration.Id],
            NativeMemberName = field.Name,
            CSharpMemberName = TypeNames.Sanitize(name),
            ReturnKind = AbiKind.Void,
            NativeReturnType = "void",
            Static = field.Static,
            Parameters = [valueParameter],
        };
        return true;
    }

    private static bool TryParseSpanLike(string canonical, out string element, out VectorParameterKind kind)
    {
        element = "";
        kind = VectorParameterKind.Span;
        var text = canonical.Trim();
        string? prefix = null;
        foreach (var candidate in new[] { "gsl::span<", "buffer_span_mut<", "buffer_span<" })
        {
            if (text.StartsWith(candidate, StringComparison.Ordinal))
            {
                prefix = candidate;
                // A buffer_span stores {begin, end}, not {size, data}.
                kind = candidate == "gsl::span<"
                    ? VectorParameterKind.Span
                    : VectorParameterKind.InitializerList;
                break;
            }
        }

        if (prefix is null)
        {
            if (!text.StartsWith("std::initializer_list<", StringComparison.Ordinal))
            {
                return false;
            }

            prefix = "std::initializer_list<";
            kind = VectorParameterKind.InitializerList;
        }

        if (!text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = SplitTemplateArguments(text[prefix.Length..^1]).ToList();
        if (arguments.Count == 0)
        {
            return false;
        }

        // A static-extent gsl::span stores only the pointer, so it does not match the
        // dynamic two-word layout the shim builds. Leave those members as stubs.
        if (prefix == "gsl::span<" && arguments.Count > 1)
        {
            var extent = arguments[1].Trim();
            if (extent is not ("-1" or "std::dynamic_extent"))
            {
                return false;
            }
        }

        element = arguments[0];
        return !string.IsNullOrWhiteSpace(element);
    }

    private static bool TryMapParameter(
        string surface,
        string name,
        string canonical,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        bool linkCarrier,
        bool symbolDispatch,
        VariantRegistry variants,
        out AbiParameter parameter,
        out string? reason)
    {
        parameter = null!;
        reason = null;
        if (string.IsNullOrWhiteSpace(canonical))
        {
            reason = "the parameter type is empty";
            return false;
        }

        var text = canonical.Trim();
        var variantType = NormalizeTypeName(text);
        if (variants.TryGet(surface, variantType, out var variantInfo))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = variantType,
                NativePointee = variantType,
                IsReference = true,
                IsConst = true,
                WrapperType = variantInfo.WrapperType,
                VariantInfo = variantInfo,
            };
            return true;
        }

        // gsl::not_null<T*> is a single-pointer struct with a defaulted copy constructor, so
        // it crosses the ABI exactly like T*. Only the pointer form unwraps; not_null of an
        // owning wrapper keeps that wrapper's ABI, which no plain pointer can stand in for.
        if (text.StartsWith("gsl::not_null<", StringComparison.Ordinal)
            && text.EndsWith(">", StringComparison.Ordinal))
        {
            var notNullInner = text["gsl::not_null<".Length..^1].Trim();
            if (IsPointerType(notNullInner))
            {
                text = notNullInner;
            }
        }

        if (!IsPointerType(text) && TryParseSharedPtr(text, out var sharedPointee))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Shared,
                NativeType = NormalizeTypeName(text),
                NativePointee = sharedPointee,
                IsReference = IsReferenceType(text),
                IsConst = IsConstType(text),
            };
            return true;
        }

        if (!IsPointerType(text) && TryParseWeakPtr(text, out var weakPointee))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Shared,
                NativeType = NormalizeTypeName(text),
                NativePointee = weakPointee,
                IsWeakPtr = true,
                IsReference = IsReferenceType(text),
                IsConst = IsConstType(text),
            };
            return true;
        }

        if (!IsPointerType(text)
            && !(IsReferenceType(text) && !IsRValueReference(text))
            && TryParseUniquePtr(text, out var uniquePointee))
        {
            var uniqueWrapper = LookupWrapperType(uniquePointee, declarationsByQualifiedName, declarationNames);
            if (uniqueWrapper is null)
            {
                reason = $"unique_ptr parameter pointee '{uniquePointee}' has no generated wrapper";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = NormalizeTypeName(text),
                NativePointee = uniquePointee,
                IsUniquePtrParam = true,
                WrapperType = uniqueWrapper,
            };
            return true;
        }

        var mapType = NormalizeTypeName(text);
        if (TryParseUnorderedMap(mapType, out var mapKey, out var mapValue))
        {
            if (!TryMapMapEntryKinds(
                    surface,
                    mapKey,
                    mapValue,
                    declarationsByQualifiedName,
                    declarationNames,
                    destroyableDeclarationIds,
                    enumUnderlyingByName,
                    variants,
                    out var mapKeyKind,
                    out var mapValueKind,
                    out var mapValueWrapper,
                    out var mapValueOwnership,
                    out var mapValueNative,
                    out var mapVariant,
                    out var mapReason))
            {
                reason = mapReason;
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.MapView,
                NativeType = mapType,
                IsMap = true,
                IsReference = IsReferenceType(text),
                IsConst = IsConstType(text),
                MapKeyKind = mapKeyKind,
                MapValueKind = mapValueKind,
                MapKeyNativeType = mapKey,
                MapValueNativeType = mapValueNative ?? mapValue,
                MapValueWrapperType = mapValueWrapper,
                MapValueOwnership = mapValueOwnership,
                VariantInfo = mapVariant,
            };
            return true;
        }

        // A span or initializer_list parameter is a two-word view over a caller-owned buffer.
        // Symbol dispatch can hand the buffer through unchanged, so the projection takes an
        // array instead of a generated C++ container.
        if (symbolDispatch && TryParseSpanLike(text, out var spanElement, out var spanKind))
        {
            var spanElementText = spanElement.Trim();
            if (spanElementText.StartsWith("gsl::not_null<", StringComparison.Ordinal)
                && spanElementText.EndsWith(">", StringComparison.Ordinal))
            {
                var inner = spanElementText["gsl::not_null<".Length..^1].Trim();
                if (IsPointerType(inner))
                {
                    spanElementText = inner;
                }
            }

            if (IsStringViewType(spanElementText) || IsStringType(spanElementText))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsVector = true,
                    VectorKind = spanKind,
                    VectorElementKind = AbiKind.StringView,
                    VectorElementType = spanElementText,
                };
                return true;
            }

            if (TryMapEnumUnderlying(spanElementText, enumUnderlyingByName, out var spanScalar)
                || TryMapFundamental(spanElementText, out spanScalar))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsVector = true,
                    VectorKind = spanKind,
                    VectorElementKind = spanScalar,
                    VectorElementType = spanElementText,
                };
                return true;
            }

            if (IsPointerType(spanElementText))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsVector = true,
                    VectorKind = spanKind,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = spanElementText,
                    VectorElementOwnership = ArrayElementOwnership.None,
                };
                return true;
            }

            reason = $"span element type '{spanElementText}' is not supported";
            return false;
        }

        if (TryParseVector(text, out var vectorElement))
        {
            if (IsStringViewType(vectorElement) || IsStringType(vectorElement))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.StringView,
                    VectorElementType = vectorElement,
                };
                return true;
            }

            if (TryMapEnumUnderlying(vectorElement, enumUnderlyingByName, out var vectorKind)
                || TryMapFundamental(vectorElement, out vectorKind))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = vectorKind,
                    VectorElementType = vectorElement,
                };
                return true;
            }

            if (IsPointerType(vectorElement))
            {
                // Pointer elements keep their pointer bits and surface as nint[].
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = vectorElement,
                    VectorElementOwnership = ArrayElementOwnership.None,
                };
                return true;
            }

            if (variants.TryGet(surface, NormalizeTypeName(vectorElement), out var vectorVariant))
            {
                var vectorVariantType = NormalizeTypeName(vectorElement);
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = vectorVariantType,
                    VectorElementWrapperType = vectorVariant.WrapperType,
                    VectorElementOwnership = ArrayElementOwnership.VariantCopy,
                    VectorElementNativeType = vectorVariantType,
                    VariantInfo = vectorVariant,
                };
                return true;
            }

            if (TryParseOptional(vectorElement, out var optionalVectorRecord)
                && TryResolveInputRecordWrapper(
                    optionalVectorRecord,
                    declarationsByQualifiedName,
                    declarationNames,
                    out var optionalVectorWrapper))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = optionalVectorRecord,
                    VectorElementWrapperType = optionalVectorWrapper,
                    VectorElementOwnership = ArrayElementOwnership.OptionalRecordCopy,
                    VectorElementNativeType = optionalVectorRecord,
                };
                return true;
            }

            if (TryResolveInputRecordWrapper(
                    vectorElement,
                    declarationsByQualifiedName,
                    declarationNames,
                    out var vectorRecordWrapper))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = vectorElement,
                    VectorElementWrapperType = vectorRecordWrapper,
                    VectorElementOwnership = ArrayElementOwnership.RecordCopy,
                    VectorElementNativeType = vectorElement,
                };
                return true;
            }

            if (TryParsePair(vectorElement, out var pairFirst, out var pairSecond)
                && TryMapPairElementKinds(pairFirst, pairSecond, enumUnderlyingByName, out var pairKeyKind, out var pairValueKind))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Vector,
                    NativeType = text,
                    IsReference = IsReferenceType(text),
                    IsConst = IsConstType(text),
                    IsVector = true,
                    VectorElementKind = AbiKind.Opaque,
                    VectorElementType = vectorElement,
                    VectorElementWrapperType = $"global::System.Collections.Generic.KeyValuePair<{CSharpMapKeyType(pairKeyKind)}, {CSharpMapValueTypeKind(pairValueKind)}>",
                    VectorElementOwnership = ArrayElementOwnership.PairCopy,
                    VectorPairKeyKind = pairKeyKind,
                    VectorPairValueKind = pairValueKind,
                    VectorPairKeyType = pairFirst,
                    VectorPairValueType = pairSecond,
                };
                return true;
            }

            reason = $"vector parameter type '{vectorElement}' is not supported";
            return false;
        }

        if (TryParseOptionalRef(text, out var optionalRefInner))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = text,
                NativePointee = optionalRefInner,
                IsOptional = true,
                IsOptionalRef = true,
                OptionalRefInnerType = optionalRefInner,
                WrapperType = LookupWrapperType(optionalRefInner, declarationsByQualifiedName, declarationNames),
            };
            return true;
        }

        if (TryParseOptional(text, out var optionalInner))
        {
            if (IsStringViewType(optionalInner) || IsStringType(optionalInner))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.StringView,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                };
                return true;
            }

            if (TryMapEnumUnderlying(optionalInner, enumUnderlyingByName, out var optionalKind)
                || TryMapFundamental(optionalInner, out optionalKind))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = optionalKind,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                };
                return true;
            }

            if (TryParseTimePoint(optionalInner, out var optionalClock, out var optionalSystemClock))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.I64,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                    TimeConversion = optionalSystemClock ? TimeConversion.SystemTimePoint : TimeConversion.ClockTimePoint,
                    TimeClockType = optionalClock,
                };
                return true;
            }

            if (TryParseDuration(optionalInner, out var optionalDuration))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.I64,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                    TimeConversion = TimeConversion.Duration,
                    TimeDurationType = optionalDuration,
                };
                return true;
            }

            if (TryParseTypeId(optionalInner, out var optionalTypeId))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.U16,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                    SpecialValue = SpecialValueKind.TypeId,
                    SpecialValueType = optionalTypeId,
                };
                return true;
            }

            if (IsStrongOrdering(optionalInner))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.I8,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                    SpecialValue = SpecialValueKind.StrongOrdering,
                };
                return true;
            }

            if (IsFilesystemPath(optionalInner))
            {
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.StringView,
                    NativeType = optionalInner,
                    IsOptional = true,
                    OptionalInnerType = optionalInner,
                    SpecialValue = SpecialValueKind.Path,
                };
                return true;
            }

            if (variants.TryGet(surface, NormalizeTypeName(optionalInner), out var optionalVariant))
            {
                var optionalVariantType = NormalizeTypeName(optionalInner);
                parameter = new AbiParameter
                {
                    Name = name,
                    Kind = AbiKind.Opaque,
                    NativeType = optionalVariantType,
                    NativePointee = optionalVariantType,
                    IsOptional = true,
                    OptionalInnerType = optionalVariantType,
                    WrapperType = optionalVariant.WrapperType,
                    VariantInfo = optionalVariant,
                };
                return true;
            }

            if (!IsByValueRecordType(optionalInner))
            {
                reason = $"optional parameter type '{optionalInner}' is not supported";
                return false;
            }

            if ((!declarationsByQualifiedName.TryGetValue(optionalInner, out var optionalDeclaration)
                    && !declarationsByQualifiedName.TryGetValue(CompactTemplateName(optionalInner), out optionalDeclaration))
                || optionalDeclaration.Abstract
                || !optionalDeclaration.CopyConstructible)
            {
                reason = $"optional record parameter '{optionalInner}' is abstract or not copy-constructible";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = optionalInner,
                NativePointee = optionalInner,
                IsOptional = true,
                OptionalInnerType = optionalInner,
                IsOptionalRecord = true,
                WrapperType = LookupWrapperType(optionalInner, declarationsByQualifiedName, declarationNames),
            };
            return true;
        }

        if (IsStringViewType(text) || IsStringType(text))
        {
            var mutableString = IsNonConstLValueReference(text) && IsStringType(text);
            if (IsNonConstLValueReference(text) && !mutableString)
            {
                reason = "mutable string_view references are not supported";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.StringView,
                NativeType = text,
                IsRValueReference = IsRValueReference(text),
                IsMutableString = mutableString,
            };
            return true;
        }

        if (TryParseStdFunction(text, enumUnderlyingByName, out var callback, out var callbackReason))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Callback,
                NativeType = text,
                Callback = callback,
            };
            return true;
        }

        if (callbackReason is not null && IsCallbackTypeName(text))
        {
            reason = callbackReason;
            return false;
        }

        if (TryMapEnumUnderlying(text, enumUnderlyingByName, out var kind)
            || TryMapFundamental(text, out kind))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = kind,
                NativeType = text,
            };
            return true;
        }

        if (!IsPointerType(text)
            && TryParseTimePoint(text, out var parameterClock, out var parameterSystemClock))
        {
            if (IsNonConstLValueReference(text))
            {
                reason = "mutable time references are not supported";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.I64,
                NativeType = text,
                TimeConversion = parameterSystemClock ? TimeConversion.SystemTimePoint : TimeConversion.ClockTimePoint,
                TimeClockType = parameterClock,
            };
            return true;
        }

        if (!IsPointerType(text)
            && TryParseDuration(text, out var parameterDuration))
        {
            if (IsNonConstLValueReference(text))
            {
                reason = "mutable duration references are not supported";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.I64,
                NativeType = text,
                TimeConversion = TimeConversion.Duration,
                TimeDurationType = parameterDuration,
            };
            return true;
        }

        if (TryParseTypeId(text, out var parameterTypeId))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.U16,
                NativeType = text,
                SpecialValue = SpecialValueKind.TypeId,
                SpecialValueType = parameterTypeId,
            };
            return true;
        }

        if (IsStrongOrdering(text))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.I8,
                NativeType = text,
                SpecialValue = SpecialValueKind.StrongOrdering,
            };
            return true;
        }

        if (IsFilesystemPath(text))
        {
            if (IsNonConstLValueReference(text))
            {
                reason = "mutable path references are not supported";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.StringView,
                NativeType = text,
                SpecialValue = SpecialValueKind.Path,
            };
            return true;
        }

        // A bitset travels as the integer its storage holds. A reference maps onto the same
        // value; a pointer does not and stays opaque. The storage is read, so a mutable
        // reference would lose the write back.
        var bitsetText = StripTopLevelQualifiers(text).TrimEnd();
        if (TryParseBitset(bitsetText.TrimEnd('&', ' '), out var bitsetBits))
        {
            if (IsNonConstLValueReference(text))
            {
                reason = "mutable bitset references are not supported";
                return false;
            }

            // Match the field width so a caller passes the same scalar type it reads back.
            parameter = new AbiParameter
            {
                Name = name,
                Kind = bitsetBits <= 8 ? AbiKind.U8
                    : bitsetBits <= 16 ? AbiKind.U16
                    : bitsetBits <= 32 ? AbiKind.U32
                    : AbiKind.U64,
                NativeType = text,
                IsBitset = true,
                BitsetBits = bitsetBits,
            };
            return true;
        }

        if (TryMapOpaque(text, declarationsByQualifiedName, declarationNames, out var pointee, out var wrapperType))
        {
            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = text,
                NativePointee = pointee,
                IsReference = IsReferenceType(text),
                IsRValueReference = IsRValueReference(text),
                IsConst = IsConstType(text),
                WrapperType = wrapperType,
            };
            return true;
        }

        if (linkCarrier && IsByValueRecordType(text))
        {
            var byValueName = StripTopLevelQualifiers(text);
            if (!declarationsByQualifiedName.TryGetValue(byValueName, out var byValueDeclaration))
            {
                reason = $"by-value record parameter '{byValueName}' is not a known bindable declaration";
                return false;
            }

            if (byValueDeclaration.Abstract)
            {
                reason = $"by-value record parameter '{byValueName}' is abstract";
                return false;
            }

            if (!byValueDeclaration.CopyConstructible)
            {
                reason = $"by-value record parameter '{byValueName}' is not copy-constructible";
                return false;
            }

            parameter = new AbiParameter
            {
                Name = name,
                Kind = AbiKind.Opaque,
                NativeType = text,
                NativePointee = byValueName,
                ByValueDeclarationId = byValueDeclaration.Id,
                IsByValueRecord = true,
                ByValueTriviallyCopyable = byValueDeclaration.TriviallyCopyable,
                ByValueTriviallyDestructible = byValueDeclaration.TriviallyDestructible,
                IsReference = true,
                IsConst = true,
                WrapperType = LookupWrapperType(text, declarationsByQualifiedName, declarationNames),
            };
            return true;
        }

        if (!linkCarrier && IsByValueRecordType(text))
        {
            reason = $"by-value record parameter '{text}' requires --link-carrier or --virtual-dispatch";
            return false;
        }

        reason = $"parameter type '{text}' is not supported";
        return false;
    }

    private static bool TryResolveInputRecordWrapper(
        string nativeType,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        out string wrapperType)
    {
        wrapperType = "";
        if (!declarationsByQualifiedName.TryGetValue(nativeType, out var declaration)
            || declaration.Access != "public"
            || declaration.Abstract
            || !declaration.CopyConstructible
            || !declarationNames.TryGetValue(declaration.Id, out var declarationName))
        {
            return false;
        }

        wrapperType = "global::" + declarationName.NamespaceName + "." + declarationName.ClassName;
        return true;
    }

    private static bool TryResolveRecordWrapper(
        string nativeType,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlySet<string> destroyableDeclarationIds,
        bool allowAbstract,
        out string wrapperType)
    {
        wrapperType = "";
        if (!declarationsByQualifiedName.TryGetValue(nativeType, out var declaration)
            || declaration.Access != "public"
            || (!allowAbstract && declaration.Abstract)
            || (!allowAbstract && !declaration.CopyConstructible)
            || !destroyableDeclarationIds.Contains(declaration.Id)
            || !declarationNames.TryGetValue(declaration.Id, out var declarationName))
        {
            return false;
        }

        wrapperType = "global::" + declarationName.NamespaceName + "." + declarationName.ClassName;
        return true;
    }

    private static string? LookupWrapperType(
        string text,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames)
    {
        var name = StripTopLevelQualifiers(text);
        while (name.EndsWith("&", StringComparison.Ordinal) || name.EndsWith("*", StringComparison.Ordinal))
        {
            name = StripTopLevelQualifiers(name[..^1].TrimEnd());
        }

        return declarationsByQualifiedName.TryGetValue(name, out var declaration)
            && declarationNames.TryGetValue(declaration.Id, out var declarationName)
                ? "global::" + declarationName.NamespaceName + "." + declarationName.ClassName
                : null;
    }

    private static bool TryMapOpaque(
        string text,
        IReadOnlyDictionary<string, DeclarationRecord> declarationsByQualifiedName,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        out string? pointee,
        out string? wrapperType)
    {
        pointee = null;
        wrapperType = null;
        if (!IsPointerOrReferenceType(text))
        {
            return false;
        }

        pointee = ExtractPointee(text);
        if (pointee.Length == 0)
        {
            return false;
        }

        // Only a single-level pointer or lvalue reference can carry a typed wrapper; deeper
        // indirection stays a raw nint so the ABI level is not silently collapsed.
        var core = StripTopLevelQualifiers(text);
        var isSinglePointer = core.EndsWith("*", StringComparison.Ordinal);
        var isRValueReference = core.EndsWith("&&", StringComparison.Ordinal);
        var isLValueReference = core.EndsWith("&", StringComparison.Ordinal) && !isRValueReference;
        if (isSinglePointer || isLValueReference || isRValueReference)
        {
            var suffixLength = isRValueReference ? 2 : 1;
            var wrapperPointee = StripTopLevelQualifiers(core[..^suffixLength].TrimEnd());
            if (!wrapperPointee.EndsWith("*", StringComparison.Ordinal)
                && !wrapperPointee.EndsWith("&", StringComparison.Ordinal)
                && declarationsByQualifiedName.TryGetValue(wrapperPointee, out var declaration)
                && declarationNames.TryGetValue(declaration.Id, out var name))
            {
                wrapperType = "global::" + name.NamespaceName + "." + name.ClassName;
            }
        }

        return true;
    }

    private static bool IsCallbackTypeName(string text)
    {
        var core = NormalizeTypeName(text);
        return CallbackTypePrefixes.Any(prefix => core.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool TryParseStdFunction(
        string canonical,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiCallback callback,
        out string? reason)
    {
        callback = null!;
        reason = null;
        var text = canonical.Trim();
        if (IsPointerType(text) || IsArrayType(text))
        {
            reason = $"callback type '{canonical}' is not supported";
            return false;
        }

        var core = NormalizeTypeName(text);
        var prefix = CallbackTypePrefixes.FirstOrDefault(candidate =>
            core.StartsWith(candidate, StringComparison.Ordinal) && core.EndsWith(">", StringComparison.Ordinal));
        if (prefix is null)
        {
            reason = $"callback type '{canonical}' is not supported";
            return false;
        }

        var inner = core[prefix.Length..^1].Trim();
        var paren = -1;
        var depth = 0;
        for (var index = 0; index < inner.Length; index++)
        {
            switch (inner[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case '(' when depth == 0:
                    paren = index;
                    index = inner.Length;
                    break;
            }
        }

        if (paren < 0)
        {
            reason = $"callback type '{canonical}' is not supported";
            return false;
        }

        var returnType = inner[..paren].Trim();
        AbiKind returnKind;
        if (string.Equals(returnType, "void", StringComparison.Ordinal))
        {
            returnKind = AbiKind.Void;
        }
        else if (!TryMapEnumUnderlying(returnType, enumUnderlyingByName, out returnKind)
            && !TryMapFundamental(returnType, out returnKind))
        {
            // String, pointer, reference, and record results need a richer callback ABI.
            reason = $"callback return type '{returnType}' is not supported";
            return false;
        }

        var close = inner.IndexOf(')', paren + 1);
        if (close < 0)
        {
            reason = $"callback type '{canonical}' is not supported";
            return false;
        }

        var arguments = new List<AbiCallbackArgument>();
        var argumentsText = inner[(paren + 1)..close].Trim();
        if (argumentsText.Length > 0)
        {
            foreach (var argumentText in SplitTemplateArguments(argumentsText))
            {
                if (!TryMapCallbackArgument(
                        argumentText.Trim(),
                        enumUnderlyingByName,
                        out var argument,
                        out var argumentReason))
                {
                    reason = argumentReason;
                    return false;
                }

                arguments.Add(argument);
            }
        }

        callback = new AbiCallback
        {
            NativeType = core,
            ReturnNativeType = returnType,
            ReturnKind = returnKind,
            Arguments = arguments,
        };
        return true;
    }

    private static bool TryMapCallbackArgument(
        string canonical,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiCallbackArgument argument,
        out string? reason)
    {
        argument = null!;
        reason = null;
        var text = canonical.Trim();
        if (text.Length == 0)
        {
            reason = "the callback parameter type is empty";
            return false;
        }

        if (IsStringViewType(text) || IsStringType(text))
        {
            argument = new AbiCallbackArgument { NativeType = text, Kind = AbiKind.StringView };
            return true;
        }

        if (TryMapEnumUnderlying(text, enumUnderlyingByName, out var kind)
            || TryMapFundamental(text, out kind))
        {
            argument = new AbiCallbackArgument { NativeType = text, Kind = kind };
            return true;
        }

        if (IsReferenceType(text) && IsConstType(text))
        {
            var inner = NormalizeTypeName(text);
            if (IsStringViewType(inner) || IsStringType(inner))
            {
                argument = new AbiCallbackArgument { NativeType = text, Kind = AbiKind.StringView };
                return true;
            }

            if (TryMapEnumUnderlying(inner, enumUnderlyingByName, out var referenceKind)
                || TryMapFundamental(inner, out referenceKind))
            {
                argument = new AbiCallbackArgument { NativeType = text, Kind = referenceKind };
                return true;
            }
        }

        if (IsPointerType(text))
        {
            argument = new AbiCallbackArgument
            {
                NativeType = text,
                Kind = AbiKind.Opaque,
                IsPointer = true,
                IsConst = IsConstType(text),
            };
            return true;
        }

        if (IsReferenceType(text))
        {
            argument = new AbiCallbackArgument
            {
                NativeType = text,
                Kind = AbiKind.Opaque,
                IsReference = true,
                IsConst = IsConstType(text),
            };
            return true;
        }

        if (!IsByValueRecordType(text)
            || IsCallbackTypeName(text))
        {
            reason = $"callback parameter type '{text}' is not supported";
            return false;
        }

        argument = new AbiCallbackArgument { NativeType = text, Kind = AbiKind.Opaque };
        return true;
    }

    private static bool TryMapEnumUnderlying(
        string canonical,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiKind kind)
    {
        kind = AbiKind.Void;
        var text = canonical.Trim();
        if (text.StartsWith("const ", StringComparison.Ordinal))
        {
            text = text["const ".Length..].TrimStart();
        }

        if (text.EndsWith(" const", StringComparison.Ordinal))
        {
            text = text[..^" const".Length].TrimEnd();
        }

        return !IsPointerOrReferenceType(text)
            && enumUnderlyingByName.TryGetValue(text, out var underlying)
            && TryMapFundamental(underlying, out kind);
    }

    private static bool TryMapFundamental(string canonical, out AbiKind kind)
    {
        var text = canonical.Trim();
        if (text.StartsWith("const ", StringComparison.Ordinal))
        {
            text = text["const ".Length..].TrimStart();
        }

        if (text.EndsWith(" const", StringComparison.Ordinal))
        {
            text = text[..^" const".Length].TrimEnd();
        }

        kind = text switch
        {
            "void" => AbiKind.Void,
            "bool" => AbiKind.Bool,
            "char" or "signed char" or "int8_t" => AbiKind.I8,
            "unsigned char" or "uint8_t" or "char8_t" => AbiKind.U8,
            "short" or "short int" or "signed short" or "signed short int" or "int16_t" => AbiKind.I16,
            "unsigned short" or "unsigned short int" or "uint16_t" => AbiKind.U16,
            "int" or "signed" or "signed int" or "int32_t" => AbiKind.I32,
            "unsigned" or "unsigned int" or "uint32_t" => AbiKind.U32,
            "long long" or "long long int" or "signed long long" or "int64_t" or "__int64" => AbiKind.I64,
            "unsigned long long" or "unsigned long long int" or "uint64_t" or "unsigned __int64" => AbiKind.U64,
            "size_t" or "std::size_t" => AbiKind.USize,
            "ptrdiff_t" or "std::ptrdiff_t" => AbiKind.ISize,
            "float" => AbiKind.F32,
            "double" => AbiKind.F64,
            _ => AbiKind.Void,
        };
        return kind != AbiKind.Void || string.Equals(text, "void", StringComparison.Ordinal);
    }

    private static bool IsPointerOrReferenceType(string canonical)
    {
        var text = StripTopLevelQualifiers(canonical);
        return text.EndsWith("&", StringComparison.Ordinal)
            || text.EndsWith("*", StringComparison.Ordinal);
    }

    internal static bool IsReferenceType(string canonical) =>
        StripTopLevelQualifiers(canonical).EndsWith("&", StringComparison.Ordinal);

    internal static bool IsPointerType(string canonical) =>
        StripTopLevelQualifiers(canonical).EndsWith("*", StringComparison.Ordinal);

    internal static bool IsRValueReference(string canonical) =>
        StripTopLevelQualifiers(canonical).EndsWith("&&", StringComparison.Ordinal);

    internal static bool IsArrayType(string canonical) =>
        StripTopLevelQualifiers(canonical).Contains('[', StringComparison.Ordinal);

    private static bool IsByValueRecordType(string canonical)
    {
        var text = StripTopLevelQualifiers(canonical);
        if (text.Length == 0
            || string.Equals(text, "void", StringComparison.Ordinal)
            || text.Contains('(', StringComparison.Ordinal)
            || text.Contains(')', StringComparison.Ordinal)
            || text.Contains('[', StringComparison.Ordinal)
            || text.Contains(']', StringComparison.Ordinal)
            || text.Contains("...", StringComparison.Ordinal)
            || text.StartsWith("decltype", StringComparison.Ordinal)
            || text.EndsWith("*", StringComparison.Ordinal)
            || text.EndsWith("&", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsNonConstLValueReference(string canonical)
    {
        var text = canonical.Trim();
        if (!text.EndsWith("&", StringComparison.Ordinal)
            || text.EndsWith("&&", StringComparison.Ordinal))
        {
            return false;
        }

        // The demangled spelling puts const on the referent (`T const&`), while headers
        // and the C++ printer may put it on the reference (`const T&`). Check both.
        var referent = text[..^1].TrimEnd();
        return !IsConstType(text) && !IsConstType(referent);
    }

    private static bool IsConstType(string canonical)
    {
        var text = canonical.Trim();
        return text.StartsWith("const ", StringComparison.Ordinal)
            || text.Contains(" const ", StringComparison.Ordinal)
            || text.EndsWith(" const", StringComparison.Ordinal);
    }

    private static string ExtractPointee(string canonical)
    {
        var text = StripTopLevelQualifiers(canonical);
        while (text.EndsWith("&", StringComparison.Ordinal) || text.EndsWith("*", StringComparison.Ordinal))
        {
            text = StripTopLevelQualifiers(text[..^1].TrimEnd());
        }

        return text;
    }

    private static bool IsCopyConstructor(MemberRecord member, string qualifiedName)
    {
        if (member.Signature is null || member.Signature.Parameters.Count != 1)
        {
            return false;
        }

        var type = member.Signature.Parameters[0].Type.Trim();
        while (type.EndsWith("&", StringComparison.Ordinal))
        {
            type = type[..^1].TrimEnd();
        }

        type = type
            .Replace("const ", "", StringComparison.Ordinal)
            .Replace(" const", "", StringComparison.Ordinal)
            .Replace("volatile ", "", StringComparison.Ordinal)
            .Replace(" volatile", "", StringComparison.Ordinal)
            .Trim();
        return string.Equals(type, qualifiedName, StringComparison.Ordinal);
    }

    private static void CollectInheritedSymbolFields(
        string ownerId,
        long offsetBits,
        HashSet<string> path,
        int depth,
        IReadOnlyDictionary<string, List<BaseInfo>> basesByDeclaration,
        IReadOnlyDictionary<string, List<MemberRecord>> membersByDeclaration,
        Dictionary<string, (MemberRecord Field, long BitOffset)> fields)
    {
        if (depth > 8 || !basesByDeclaration.TryGetValue(ownerId, out var bases))
        {
            return;
        }

        foreach (var baseInfo in bases)
        {
            if (!path.Add(baseInfo.DeclarationId))
            {
                continue;
            }

            var baseOffsetBits = offsetBits + baseInfo.Offset * 8;
            if (membersByDeclaration.TryGetValue(baseInfo.DeclarationId, out var baseMembers))
            {
                foreach (var field in baseMembers.Where(member =>
                             member.Kind == "field"
                             && member.Access == "public"
                             && !string.IsNullOrWhiteSpace(member.Name)))
                {
                    fields.TryAdd(field.Id, (field, baseOffsetBits + (field.BitOffset ?? 0)));
                }
            }

            CollectInheritedSymbolFields(
                baseInfo.DeclarationId,
                baseOffsetBits,
                path,
                depth + 1,
                basesByDeclaration,
                membersByDeclaration,
                fields);
            path.Remove(baseInfo.DeclarationId);
        }
    }

    private static bool TryPlanSymbolField(
        DeclarationRecord declaration,
        MemberRecord field,
        long bitOffset,
        string memberId,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        IReadOnlyDictionary<string, DeclarationRecord> publicDeclarationsByQualifiedName,
        IReadOnlyDictionary<string, string> enumUnderlyingByName,
        out AbiFunction getter,
        out AbiFunction? setter,
        out string? reason)
    {
        getter = null!;
        setter = null;
        reason = null;
        if (string.IsNullOrWhiteSpace(field.Name))
        {
            reason = "anonymous fields are not projected";
            return false;
        }

        var bitfield = field.BitWidth > 0;

        if (field.Static)
        {
            if (field.Type is null || string.IsNullOrWhiteSpace(field.Type.Canonical))
            {
                reason = "the field type is unavailable";
                return false;
            }

            if (field.Symbol is null && field.Rva is null && field.Address is null)
            {
                reason = "the static field has no symbol in the IR";
                return false;
            }

            var staticCanonical = NormalizeTypeName(field.Type.Canonical);
            AbiKind staticKind;
            string? staticWrapperType = null;
            var staticFallback = false;
            if (TryMapEnumUnderlying(staticCanonical, enumUnderlyingByName, out staticKind)
                || TryMapFundamental(staticCanonical, out staticKind))
            {
            }
            else if (IsStringViewType(field.Type.Canonical)
                || IsStringType(field.Type.Canonical)
                || string.Equals(staticCanonical, "char[]", StringComparison.Ordinal))
            {
                staticKind = AbiKind.StringView;
            }
            else if (IsPointerType(staticCanonical))
            {
                staticKind = AbiKind.Opaque;
            }
            else if (publicDeclarationsByQualifiedName.TryGetValue(staticCanonical, out var staticDeclaration)
                && declarationNames.TryGetValue(staticDeclaration.Id, out var staticName))
            {
                staticKind = AbiKind.Opaque;
                staticWrapperType = "global::" + staticName.NamespaceName + "." + staticName.ClassName;
            }
            else
            {
                // The field offset or symbol is still useful even when the type cannot be
                // projected. Expose the storage address as a read-only nint.
                staticKind = AbiKind.Opaque;
                staticFallback = true;
            }

            getter = new AbiFunction
            {
                CppName = "hs_get_" + CSharpNaming.ShortHash(memberId + "#" + declaration.Id),
                Kind = AbiFunctionKind.Get,
                Surface = declaration.Surface,
                DeclarationId = declaration.Id,
                MemberId = memberId,
                ClassCppName = declaration.QualifiedName,
                ClassName = declarationNames[declaration.Id],
                NativeMemberName = field.Name,
                CSharpMemberName = TypeNames.Sanitize(field.Name),
                ReturnKind = staticKind,
                NativeReturnType = staticCanonical,
                ReturnWrapperType = staticWrapperType,
                FieldAddress = staticFallback,
                Static = true,
            };
            return true;
        }

        if (bitOffset < 0 || (!bitfield && bitOffset % 8 != 0))
        {
            reason = "the field has no byte-aligned offset";
            return false;
        }

        if (field.Type is null || string.IsNullOrWhiteSpace(field.Type.Canonical))
        {
            reason = "the field type is unavailable";
            return false;
        }

        var canonical = field.Type.Canonical.Trim();
        var isConst = IsConstType(canonical);
        AbiKind kind;
        var fallback = false;
        var bitsetBits = 0;
        if (TryMapEnumUnderlying(canonical, enumUnderlyingByName, out kind)
            || TryMapFundamental(canonical, out kind))
        {
        }
        else if (TryParseBitset(StripTopLevelQualifiers(canonical).TrimEnd().TrimEnd('&', ' '), out bitsetBits))
        {
            // std::bitset is bit-packed; a byte-aligned field aliases the storage as an
            // integer of its size, which is what the shim reads and writes.
            kind = bitsetBits <= 8 ? AbiKind.U8
                : bitsetBits <= 16 ? AbiKind.U16
                : bitsetBits <= 32 ? AbiKind.U32
                : AbiKind.U64;
        }
        else if (IsPointerType(canonical))
        {
            kind = AbiKind.Opaque;
        }
        else
        {
            // The offset is known, so expose the field storage as a read-only nint even
            // when the value type itself cannot be projected.
            kind = AbiKind.Opaque;
            fallback = true;
        }

        // A bitfield is only readable as the scalar its bits encode.
        if (bitfield && fallback)
        {
            reason = "the bitfield type is not supported by symbol dispatch";
            return false;
        }

        var className = declarationNames[declaration.Id];
        getter = new AbiFunction
        {
            CppName = "hs_get_" + CSharpNaming.ShortHash(memberId + "#" + declaration.Id),
            Kind = AbiFunctionKind.Get,
            Surface = declaration.Surface,
            DeclarationId = declaration.Id,
            MemberId = memberId,
            ClassCppName = declaration.QualifiedName,
            ClassName = className,
            NativeMemberName = field.Name,
            CSharpMemberName = TypeNames.Sanitize(field.Name),
            ReturnKind = kind,
            NativeReturnType = canonical,
            FieldBitOffset = bitOffset,
            FieldBitWidth = bitfield ? (int)field.BitWidth!.Value : null,
            FieldAddress = fallback,
            IsBitset = bitsetBits > 0,
            BitsetBits = bitsetBits,
        };
        if (!isConst && !fallback)
        {
            setter = new AbiFunction
            {
                CppName = "hs_set_" + CSharpNaming.ShortHash(memberId + "#" + declaration.Id),
                Kind = AbiFunctionKind.Set,
                Surface = declaration.Surface,
                DeclarationId = declaration.Id,
                MemberId = memberId,
                ClassCppName = declaration.QualifiedName,
                ClassName = className,
                NativeMemberName = field.Name,
                CSharpMemberName = TypeNames.Sanitize(field.Name),
                ReturnKind = AbiKind.Void,
                NativeReturnType = "void",
                FieldBitOffset = bitOffset,
                FieldBitWidth = bitfield ? (int)field.BitWidth!.Value : null,
                Parameters =
                [
                    new AbiParameter
                    {
                        Name = "value",
                        Kind = kind,
                        NativeType = canonical,
                        IsBitset = bitsetBits > 0,
                        BitsetBits = bitsetBits,
                    },
                ],
            };
        }

        return true;
    }

    /// <summary>
    /// Symbol dispatch calls through a raw function pointer without compiling against the
    /// declaring header. Only parameters and returns that can be expressed by pointer bits or
    /// fixed-width scalars are safe; records are passed as <c>void*</c>, never by value.
    /// </summary>
    private static bool IsSymbolDispatchSupported(
        AbiFunction function,
        IReadOnlyDictionary<string, DeclarationRecord> symbolByValueReturns,
        IReadOnlyDictionary<string, DeclarationRecord> symbolVectorElements,
        IReadOnlyDictionary<string, DeclarationRecord> symbolOptionalRecords,
        out string? reason)
    {
        reason = null;
        if (function.VariantInfo is not null
            || function.CallableInfo is not null
            || function.VariantOp != VariantOp.None
            || function.CallableOp != CallableOp.None)
        {
            reason = "variants and callable wrappers are not supported by symbol dispatch";
            return false;
        }

        var ownedRecordReturn = function.ReturnOwned
            && function.ReturnKind == AbiKind.Opaque
            && !IsSingleLevelPointerOrReference(function.NativeReturnType);
        var optionalRefReturn = function.ReturnOptional
            && function.ReturnKind == AbiKind.Opaque
            && !function.ReturnOwned;
        var optionalValueReturn = function.ReturnOptional
            && !optionalRefReturn
            && (function.ReturnKind == AbiKind.StringView
                || IsSymbolScalarKind(function.ReturnKind)
                || function.ReturnOptionalRecord);
        if (function.ReturnExpected
            || (function.ReturnOptional && !optionalRefReturn && !optionalValueReturn)
            || (function.ReturnOwned && !ownedRecordReturn && !function.ReturnUniquePtr)
            || function.SharedWrapper is not (SharedWrapperKind.None or SharedWrapperKind.SharedPtr
                or SharedWrapperKind.WeakPtr or SharedWrapperKind.NonOwnerPtr)
            || function.TimeConversion != TimeConversion.None
            || function.SpecialValue != SpecialValueKind.None
            || function.MapKeyKind != AbiKind.Void
            || function.MapValueKind != AbiKind.Void)
        {
            reason = "the return type needs a generated C++ type";
            return false;
        }

        if (function.ReturnArray && !IsSymbolVectorElementSupported(
                function.ArrayElementKind,
                function.ArrayElementOwnership,
                function.ArrayElementNativeType,
                symbolVectorElements))
        {
            reason = "the vector element type is not supported by symbol dispatch";
            return false;
        }

        switch (function.ReturnKind)
        {
            case AbiKind.Void:
            case AbiKind.Bool:
            case AbiKind.I8:
            case AbiKind.U8:
            case AbiKind.I16:
            case AbiKind.U16:
            case AbiKind.I32:
            case AbiKind.U32:
            case AbiKind.I64:
            case AbiKind.U64:
            case AbiKind.USize:
            case AbiKind.ISize:
            case AbiKind.F32:
            case AbiKind.F64:
            case AbiKind.StringView:
            case AbiKind.ArrayView:
            case AbiKind.Shared:
                break;
            case AbiKind.Opaque:
                if (!ownedRecordReturn
                    && !optionalRefReturn
                    && !IsSingleLevelPointerOrReference(function.NativeReturnType))
                {
                    reason = "by-value record returns are not supported by symbol dispatch";
                    return false;
                }

                break;
            default:
                reason = "the return type is not supported by symbol dispatch";
                return false;
        }

        foreach (var parameter in function.Parameters)
        {
            var optionalRefParameter = parameter.IsOptionalRef && parameter.Kind == AbiKind.Opaque;
            var optionalRecordParameter = parameter.IsOptional
                && !optionalRefParameter
                && parameter.IsOptionalRecord
                && parameter.OptionalInnerType is { } optionalInner
                && symbolOptionalRecords.ContainsKey(CompactTemplateName(StripTopLevelQualifiers(optionalInner)));
            var optionalValueParameter = parameter.IsOptional
                && !optionalRefParameter
                && (parameter.Kind == AbiKind.StringView
                    || IsSymbolScalarKind(parameter.Kind)
                    || optionalRecordParameter);
            if ((parameter.IsOptional && !optionalRefParameter && !optionalValueParameter)
                || (parameter.IsOptionalRef && !optionalRefParameter)
                || parameter.IsUniquePtrParam
                || parameter.IsMap
                || parameter.VariantInfo is not null
                || parameter.Callback is not null
                || parameter.TimeConversion != TimeConversion.None
                || parameter.SpecialValue != SpecialValueKind.None)
            {
                reason = "the parameter needs a generated C++ type";
                return false;
            }

            if (optionalRefParameter)
            {
                continue;
            }

            if (parameter.IsVector)
            {
                // A by-value vector parameter is destroyed by the callee under the MSVC ABI;
                // the shim's local vector would then be destroyed twice. References and
                // pointers stay owned by the shim for the duration of the call. A span or an
                // initializer_list owns nothing, so by-value is exactly what the callee takes.
                if (parameter.VectorKind == VectorParameterKind.Vector
                    && !IsReferenceType(parameter.NativeType)
                    && !IsPointerType(parameter.NativeType))
                {
                    reason = "by-value vector parameters are not supported by symbol dispatch";
                    return false;
                }

                if (!IsSymbolVectorElementSupported(
                        parameter.VectorElementKind,
                        parameter.VectorElementOwnership,
                        parameter.VectorElementNativeType,
                        symbolVectorElements))
                {
                    reason = "the vector element type is not supported by symbol dispatch";
                    return false;
                }

                continue;
            }

            switch (parameter.Kind)
            {
                case AbiKind.Bool:
                case AbiKind.I8:
                case AbiKind.U8:
                case AbiKind.I16:
                case AbiKind.U16:
                case AbiKind.I32:
                case AbiKind.U32:
                case AbiKind.I64:
                case AbiKind.U64:
                case AbiKind.USize:
                case AbiKind.ISize:
                case AbiKind.F32:
                case AbiKind.F64:
                case AbiKind.Shared:
                    break;
                case AbiKind.StringView:
                    if (!IsStringType(parameter.NativeType) && !IsStringViewType(parameter.NativeType))
                    {
                        reason = "only std::string and std::string_view parameters are supported";
                        return false;
                    }

                    break;
                case AbiKind.Opaque:
                    if (optionalRecordParameter)
                    {
                        break;
                    }

                    if (parameter.IsByValueRecord)
                    {
                        if (parameter.NativePointee is null
                            || !symbolByValueReturns.TryGetValue(parameter.NativePointee, out var byValueRecord))
                        {
                            reason = "the by-value record parameter has no eligible layout";
                            return false;
                        }

                        // The callee destroys non-trivial by-value parameters. A byte copy of
                        // an owning record would then release the same resources as the source,
                        // so those parameters are copied through the record's copy constructor.
                        if (!byValueRecord.TriviallyDestructible && !byValueRecord.CopyConstructible)
                        {
                            reason = "by-value record parameter is not copy-constructible";
                            return false;
                        }

                        break;
                    }

                    if (!IsSingleLevelPointerOrReference(parameter.NativeType))
                    {
                        reason = "by-value record parameters are not supported by symbol dispatch";
                        return false;
                    }

                    break;
                default:
                    reason = "the parameter type is not supported by symbol dispatch";
                    return false;
            }
        }

        return true;
    }

    private static bool TryResolveSymbolOptionalRecord(
        string inner,
        IReadOnlyDictionary<string, DeclarationRecord> symbolOptionalRecords,
        IReadOnlyDictionary<string, DeclarationName> declarationNames,
        out DeclarationRecord record,
        out string wrapperType)
    {
        record = null!;
        wrapperType = "";
        var name = CompactTemplateName(StripTopLevelQualifiers(inner));
        if (!symbolOptionalRecords.TryGetValue(name, out var found))
        {
            return false;
        }

        if (!declarationNames.TryGetValue(found.Id, out var declarationName))
        {
            return false;
        }

        record = found;
        wrapperType = "global::" + declarationName.NamespaceName + "." + declarationName.ClassName;
        return true;
    }

    internal static bool IsSymbolScalarKind(AbiKind kind) => kind is
        AbiKind.Bool or AbiKind.I8 or AbiKind.U8
        or AbiKind.I16 or AbiKind.U16 or AbiKind.I32 or AbiKind.U32
        or AbiKind.I64 or AbiKind.U64 or AbiKind.USize or AbiKind.ISize
        or AbiKind.F32 or AbiKind.F64;

    private static bool IsSymbolVectorElementSupported(
        AbiKind elementKind,
        ArrayElementOwnership ownership,
        string? elementNativeType,
        IReadOnlyDictionary<string, DeclarationRecord> symbolVectorElements)
    {
        switch (elementKind)
        {
            case AbiKind.StringView:
                return true;
            case AbiKind.Opaque:
                return ownership switch
                {
                    // A single-level pointer element keeps its pointer bits.
                    ArrayElementOwnership.None => true,
                    // The shim copies record elements into an aligned byte blob.
                    ArrayElementOwnership.RecordCopy => elementNativeType is not null
                        && symbolVectorElements.ContainsKey(elementNativeType),
                    _ => false,
                };
            case AbiKind.Bool:
                // std::vector<bool> is a bit-packed specialization.
                return false;
            case AbiKind.I8:
            case AbiKind.U8:
            case AbiKind.I16:
            case AbiKind.U16:
            case AbiKind.I32:
            case AbiKind.U32:
            case AbiKind.I64:
            case AbiKind.U64:
            case AbiKind.USize:
            case AbiKind.ISize:
            case AbiKind.F32:
            case AbiKind.F64:
                return true;
            default:
                return false;
        }
    }

    private static bool IsSingleLevelPointerOrReference(string canonical)
    {
        var core = StripTopLevelQualifiers(canonical);
        var isPointer = core.EndsWith("*", StringComparison.Ordinal);
        var isRValueReference = core.EndsWith("&&", StringComparison.Ordinal);
        var isLValueReference = core.EndsWith("&", StringComparison.Ordinal) && !isRValueReference;
        if (!isPointer && !isLValueReference)
        {
            return false;
        }

        var pointee = StripTopLevelQualifiers(core[..^1].TrimEnd());
        return pointee.Length > 0
            && !pointee.EndsWith("*", StringComparison.Ordinal)
            && !pointee.EndsWith("&", StringComparison.Ordinal)
            && !pointee.Contains('(', StringComparison.Ordinal);
    }
}
