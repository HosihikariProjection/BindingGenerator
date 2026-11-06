// Prototype symbol-level IR extractor for Bedrock 1.21.3.
//
// Reads public symbols from a publics-only PDB and an ELF symbol table, demangles
// the symbols for a small set of classes, merges the two configurations, and
// writes a prototype JSON document. It intentionally does not emit the versioned
// IR yet; the schema is still being shaped.

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <vector>

#include "llvm/ADT/StringExtras.h"
#include "llvm/ADT/StringRef.h"
#include "llvm/BinaryFormat/ELF.h"
#include "llvm/DebugInfo/CodeView/SymbolDeserializer.h"
#include "llvm/DebugInfo/CodeView/SymbolRecord.h"
#include "llvm/DebugInfo/PDB/PDB.h"
#include "llvm/DebugInfo/PDB/Native/NativeSession.h"
#include "llvm/DebugInfo/PDB/Native/PDBFile.h"
#include "llvm/DebugInfo/PDB/Native/SymbolStream.h"
#include "llvm/Demangle/Demangle.h"
#include "llvm/Object/Binary.h"
#include "llvm/Object/COFF.h"
#include "llvm/Object/ELFObjectFile.h"
#include "llvm/Object/ObjectFile.h"
#include "llvm/Support/Error.h"
#include "llvm/Support/Format.h"
#include "llvm/Support/JSON.h"
#include "llvm/Support/SHA256.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

namespace {

struct Inputs {
    std::string Pdb;
    std::string Exe;
    std::string Elf;
    std::string Output;
    std::string IrOutput;
    std::string Version = "1.21.3";
    std::string ListClasses;
    std::vector<std::string> Classes;
    std::set<std::string> ClassSet;
    std::map<std::string, uint64_t> DiscoveredClasses;
    bool Verbose = false;
};

struct Method {
    std::string ClassName;
    std::string Name;
    std::string Key;
    std::string ReturnType;
    std::vector<std::string> Parameters;
    std::string Access = "unknown";
    bool Const = false;
    bool RefQualified = false;
    bool Static = false;
    bool Virtual = false;
    bool Ctor = false;
    bool Dtor = false;
    std::string Symbol;
    uint64_t Rva = 0;
    uint64_t Va = 0;
    int VtableIndex = -1;
};

struct SupportSymbol {
    std::string Symbol;
    uint64_t Rva = 0;
    uint64_t Address = 0;
};

struct RttiBase {
    std::string Name;
    int64_t Offset = 0;
    bool Virtual = false;
    std::string Access = "public";
};

struct Configuration {
    std::string Id;
    std::map<std::string, Method> Methods;
    // Method keys grouped by class. Scanning Methods per class is quadratic: at six
    // thousand classes and a few hundred thousand methods it dominated the whole run.
    std::map<std::string, std::set<std::string>> MethodKeysByClass;
    // Methods grouped by class and address, for the vtable slot walk. It used to rescan
    // every method for every slot of every vtable.
    std::map<std::string, std::map<uint64_t, std::vector<Method*>>> MethodsByClassAddress;
    std::map<std::string, std::string> VtableSymbols;
    std::map<std::string, uint64_t> VtableAddresses;
    std::string StringDestructorSymbol;
    uint64_t StringDestructorRva = 0;
    uint64_t StringDestructorAddress = 0;
    std::map<std::string, SupportSymbol> RecordDestructors;
    std::map<std::string, SupportSymbol> RecordCopyConstructors;
    std::map<std::string, SupportSymbol> StaticFields;
    std::map<std::string, std::vector<RttiBase>> RttiBases;
    std::map<uint64_t, uint64_t> ElfRelocations;
    std::map<std::string, uint64_t> TypeInfoByName;
    uint64_t ClassTypeInfoVtable = 0;
    uint64_t SiTypeInfoVtable = 0;
    uint64_t VmiTypeInfoVtable = 0;
};

std::string trim(StringRef Text) {
    return Text.trim().str();
}

bool startsWith(const std::string& Text, StringRef Prefix) {
    return StringRef(Text).starts_with(Prefix);
}

std::string replaceAll(std::string Text, StringRef From, StringRef To) {
    size_t Position = 0;
    while ((Position = Text.find(From.str(), Position)) != std::string::npos) {
        Text.replace(Position, From.size(), To.str());
        Position += To.size();
    }
    return Text;
}

std::string normalizeLongWords(const std::string& Text) {
    auto isWord = [](char Character) {
        return std::isalnum(static_cast<unsigned char>(Character)) != 0 || Character == '_';
    };
    auto peekWord = [&](size_t& Position) {
        while (Position < Text.size() && Text[Position] == ' ') {
            ++Position;
        }
        size_t Start = Position;
        while (Position < Text.size() && isWord(Text[Position])) {
            ++Position;
        }
        return Text.substr(Start, Position - Start);
    };

    std::string Out;
    size_t Index = 0;
    while (Index < Text.size()) {
        size_t Start = Index;
        while (Index < Text.size() && isWord(Text[Index])) {
            ++Index;
        }
        if (Start == Index) {
            Out.push_back(Text[Index]);
            ++Index;
            continue;
        }

        std::string Word = Text.substr(Start, Index - Start);
        if (Word == "unsigned") {
            size_t Next = Index;
            if (peekWord(Next) == "long") {
                size_t After = Next;
                if (peekWord(After) == "long") {
                    Out += "unsigned long long";
                    Index = After;
                } else {
                    Out += "unsigned long long";
                    Index = Next;
                }
                continue;
            }
        }
        if (Word == "long") {
            size_t Next = Index;
            std::string NextWord = peekWord(Next);
            if (NextWord == "double") {
                Out += "long";
                continue;
            }
            Out += "long long";
            if (NextWord == "long") {
                Index = Next;
            }
            continue;
        }

        Out += Word;
    }
    return Out;
}

std::string normalizeType(std::string Text, bool Windows) {
    Text = replaceAll(std::move(Text), "class ", "");
    Text = replaceAll(std::move(Text), "struct ", "");
    Text = replaceAll(std::move(Text), "enum ", "");
    Text = replaceAll(std::move(Text), "union ", "");
    Text = replaceAll(std::move(Text), "__ptr64", "");
    Text = replaceAll(std::move(Text), "__restrict", "");
    Text = replaceAll(std::move(Text), "__unaligned", "");
    Text = replaceAll(std::move(Text), "__cdecl", "");
    Text = replaceAll(std::move(Text), "__fastcall", "");
    Text = replaceAll(std::move(Text), "__stdcall", "");
    Text = replaceAll(std::move(Text), "__thiscall", "");
    Text = replaceAll(std::move(Text), "__vectorcall", "");
    Text = replaceAll(std::move(Text), "std::__1::", "std::");
    Text = replaceAll(std::move(Text), "std::__cxx11::", "std::");

    if (Windows) {
        Text = replaceAll(std::move(Text), "unsigned __int64", "unsigned long long");
        Text = replaceAll(std::move(Text), "unsigned __int32", "unsigned int");
        Text = replaceAll(std::move(Text), "unsigned __int16", "unsigned short");
        Text = replaceAll(std::move(Text), "unsigned __int8", "unsigned char");
        Text = replaceAll(std::move(Text), "__int64", "long long");
        Text = replaceAll(std::move(Text), "__int32", "int");
        Text = replaceAll(std::move(Text), "__int16", "short");
        Text = replaceAll(std::move(Text), "__int8", "signed char");
    } else {
        Text = normalizeLongWords(Text);
    }

    std::string Collapsed;
    bool PendingSpace = false;
    for (char Character : Text) {
        if (std::isspace(static_cast<unsigned char>(Character)) != 0) {
            PendingSpace = true;
            continue;
        }
        if (PendingSpace && !Collapsed.empty() && Character != '*' && Character != '&' && Character != '<'
            && Character != '>' && Character != ',' && Character != ')' && Character != ']'
            && Collapsed.back() != '(' && Collapsed.back() != '<' && Collapsed.back() != ',') {
            Collapsed.push_back(' ');
        }
        PendingSpace = false;
        Collapsed.push_back(Character);
    }
    return trim(Collapsed);
}

std::vector<std::string> splitParameters(StringRef Text) {
    std::vector<std::string> Parameters;
    size_t Depth = 0;
    size_t Start = 0;
    for (size_t Index = 0; Index < Text.size(); ++Index) {
        char Character = Text[Index];
        if (Character == '<' || Character == '(' || Character == '[') {
            ++Depth;
        } else if (Character == '>' || Character == ')' || Character == ']') {
            --Depth;
        } else if (Character == ',' && Depth == 0) {
            Parameters.push_back(trim(Text.slice(Start, Index)));
            Start = Index + 1;
        }
    }
    StringRef Last = Text.drop_front(Start).trim();
    if (!Last.empty()) {
        Parameters.push_back(Last.str());
    }
    if (Parameters.size() == 1 && Parameters[0] == "void") {
        Parameters.clear();
    }
    return Parameters;
}

struct ParsedMethod {
    bool Valid = false;
    std::string Access = "unknown";
    std::string ReturnType;
    std::string QualifiedName;
    std::string Name;
    std::vector<std::string> Parameters;
    bool Const = false;
    bool RefQualified = false;
    bool Static = false;
    bool Virtual = false;
    bool Ctor = false;
    bool Dtor = false;
};

ParsedMethod parseDemangled(const std::string& Demangled, const std::string& ClassName, bool Windows) {
    ParsedMethod Result;
    std::string Text = trim(Demangled);

    for (const char* Prefix : {"public: ", "protected: ", "private: "}) {
        if (startsWith(Text, Prefix)) {
            Result.Access = StringRef(Prefix).drop_back(2).str();
            Text = Text.substr(std::strlen(Prefix));
            break;
        }
    }

    for (;;) {
        if (startsWith(Text, "virtual ")) {
            Result.Virtual = true;
            Text = Text.substr(8);
        } else if (startsWith(Text, "static ")) {
            Result.Static = true;
            Text = Text.substr(7);
        } else {
            break;
        }
    }

    const std::string Marker = ClassName + "::";
    size_t MarkerPosition = std::string::npos;
    size_t MarkerDepth = 0;
    for (size_t Index = 0; Index + Marker.size() <= Text.size(); ++Index) {
        char Character = Text[Index];
        if (Character == '<' || Character == '(' || Character == '[') {
            ++MarkerDepth;
        } else if (Character == '>' || Character == ')' || Character == ']') {
            --MarkerDepth;
        } else if (MarkerDepth == 0 && Text.compare(Index, Marker.size(), Marker) == 0) {
            MarkerPosition = Index;
            break;
        }
    }
    if (MarkerPosition == std::string::npos) {
        return Result;
    }

    size_t NameStart = MarkerPosition + Marker.size();
    size_t OpenParen = Text.find('(', NameStart);
    if (OpenParen == std::string::npos) {
        return Result;
    }

    std::string Name = trim(StringRef(Text).slice(NameStart, OpenParen));
    if (Name.empty() || Name.find("::") != std::string::npos || Name.find(' ') != std::string::npos) {
        return Result;
    }

    Result.Name = Name;
    Result.QualifiedName = Marker + Name;
    // The class name arrives qualified (`BedrockLog::CategoryLogFile`) while the demangled
    // member name is only the last component, so a constructor of a namespaced class never
    // matched and was recorded as a plain method with an unknown return type.
    std::string LastName = ClassName;
    if (size_t Separator = LastName.rfind("::"); Separator != std::string::npos) {
        LastName = LastName.substr(Separator + 2);
    }
    Result.Ctor = Name == LastName;
    Result.Dtor = Name == "~" + LastName;

    std::string Before = trim(StringRef(Text).slice(0, MarkerPosition));
    for (const char* Convention : {"__cdecl", "__fastcall", "__stdcall", "__thiscall", "__vectorcall"}) {
        if (Before.ends_with(Convention)) {
            Before.resize(Before.size() - std::strlen(Convention));
            Before = trim(Before);
        }
    }
    if (!Result.Ctor && !Result.Dtor) {
        Result.ReturnType = normalizeType(Before, Windows);
    }

    size_t Depth = 0;
    size_t CloseParen = std::string::npos;
    for (size_t Index = OpenParen; Index < Text.size(); ++Index) {
        if (Text[Index] == '(') {
            ++Depth;
        } else if (Text[Index] == ')') {
            --Depth;
            if (Depth == 0) {
                CloseParen = Index;
                break;
            }
        }
    }
    if (CloseParen == std::string::npos) {
        return Result;
    }

    for (auto& Parameter : splitParameters(StringRef(Text).slice(OpenParen + 1, CloseParen))) {
        Result.Parameters.push_back(normalizeType(std::move(Parameter), Windows));
    }

    std::string Suffix = trim(StringRef(Text).drop_front(CloseParen + 1));
    Result.Const = StringRef(Suffix).contains("const");
    Result.RefQualified = StringRef(Suffix).contains("&&") || StringRef(Suffix).contains("&");
    Result.Valid = true;
    return Result;
}

std::string parseMsvcStaticData(const std::string& Mangled) {
    size_t At = Mangled.find('@');
    if (At == std::string::npos || At < 2) {
        return "";
    }
    size_t DoubleAt = Mangled.find("@@", At);
    if (DoubleAt == std::string::npos || DoubleAt + 2 >= Mangled.size()
        || Mangled[DoubleAt + 2] != '2') {
        return "";
    }
    std::string Field = Mangled.substr(1, At - 1);
    std::string Class = Mangled.substr(At + 1, DoubleAt - At - 1);
    if (Field.empty() || Class.empty()
        || Class.find('@') != std::string::npos
        || Class.find('?') != std::string::npos) {
        return "";
    }
    return Class + "::" + Field;
}

bool parseItaniumStaticData(const std::string& Mangled, const std::string& ClassName, std::string& Field) {
    std::string Prefix = "_ZN" + std::to_string(ClassName.size()) + ClassName;
    if (!startsWith(Mangled, Prefix)) {
        return false;
    }
    size_t Position = Prefix.size();
    size_t Length = 0;
    size_t Digits = 0;
    while (Position < Mangled.size()
           && std::isdigit(static_cast<unsigned char>(Mangled[Position])) != 0) {
        Length = Length * 10 + static_cast<size_t>(Mangled[Position] - '0');
        ++Position;
        ++Digits;
    }
    if (Digits == 0 || Mangled.back() != 'E' || Position + Length + 1 != Mangled.size()) {
        return false;
    }
    Field = Mangled.substr(Position, Length);
    return !Field.empty();
}

bool isSameRecordType(std::string Type, const std::string& Record) {
    Type = trim(Type);
    while (Type.ends_with("&")) {
        Type = trim(Type.substr(0, Type.size() - 1));
    }
    for (const char* Qualifier : {"const ", " const", "volatile ", " volatile"}) {
        size_t Position;
        while ((Position = Type.find(Qualifier)) != std::string::npos) {
            Type.erase(Position, std::strlen(Qualifier));
        }
    }
    Type = trim(Type);
    for (const char* Keyword : {"class ", "struct ", "union ", "enum "}) {
        if (startsWith(Type, Keyword)) {
            Type = trim(Type.substr(std::strlen(Keyword)));
        }
    }
    return Type == Record;
}

bool classifyRecordSupport(const std::string& Demangled, bool Windows,
                           std::string& ClassName, bool& IsDestructor, bool& IsCopyConstructor) {
    IsDestructor = false;
    IsCopyConstructor = false;
    std::string Text = trim(Demangled);
    for (const char* Prefix : {"public: ", "protected: ", "private: "}) {
        if (startsWith(Text, Prefix)) {
            Text = std::string(Text).substr(std::strlen(Prefix));
            break;
        }
    }
    for (const char* Convention : {"__cdecl ", "__fastcall ", "__stdcall ", "__thiscall ", "__vectorcall ", "virtual "}) {
        size_t Position;
        while ((Position = Text.find(Convention)) != std::string::npos) {
            Text.erase(Position, std::strlen(Convention));
        }
    }

    size_t OpenParen = Text.find('(');
    if (OpenParen == std::string::npos) {
        return false;
    }

    std::string Head = trim(StringRef(Text).slice(0, OpenParen));
    size_t Separator = Head.rfind("::");
    if (Separator == std::string::npos) {
        return false;
    }

    ClassName = trim(StringRef(Head).slice(0, Separator));
    std::string Name = trim(Head.substr(Separator + 2));
    size_t LastSeparator = ClassName.rfind("::");
    std::string LastName = LastSeparator == std::string::npos
        ? ClassName
        : ClassName.substr(LastSeparator + 2);
    if (Name == "~" + LastName) {
        IsDestructor = true;
        return true;
    }
    if (Name != LastName) {
        return false;
    }

    ParsedMethod Parsed = parseDemangled(Text, ClassName, Windows);
    if (!Parsed.Valid || !Parsed.Ctor || Parsed.Parameters.size() != 1) {
        return false;
    }
    IsCopyConstructor = isSameRecordType(Parsed.Parameters[0], ClassName);
    return IsCopyConstructor;
}

void addTypeTokens(const std::string& Text, std::set<std::string>& Tokens) {
    std::string Current;
    for (char Character : Text) {
        auto Value = static_cast<unsigned char>(Character);
        if (std::isalnum(Value) != 0 || Character == '_' || Character == ':') {
            Current.push_back(Character);
        } else if (!Current.empty()) {
            Tokens.insert(Current);
            Current.clear();
        }
    }
    if (!Current.empty()) {
        Tokens.insert(Current);
    }
}

std::string makeKey(const ParsedMethod& Method) {
    std::string Key = Method.QualifiedName + "(";
    for (size_t Index = 0; Index < Method.Parameters.size(); ++Index) {
        if (Index != 0) {
            Key += ",";
        }
        Key += Method.Parameters[Index];
    }
    Key += ")";
    if (Method.Const) {
        Key += "const";
    }
    return Key;
}

void addMethod(Configuration& Config, const ParsedMethod& Parsed, const std::string& Symbol, uint64_t Rva, uint64_t Va) {
    Method Method;
    // The owner is the scope the member name sits in, so a nested class keeps its own
    // members instead of having them attributed to the outermost class.
    auto OwnerSeparator = Parsed.QualifiedName.rfind("::");
    Method.ClassName = OwnerSeparator == std::string::npos
        ? Parsed.QualifiedName
        : Parsed.QualifiedName.substr(0, OwnerSeparator);
    Method.Name = Parsed.Name;
    Method.Key = makeKey(Parsed);
    Method.ReturnType = Parsed.ReturnType;
    Method.Parameters = Parsed.Parameters;
    Method.Access = Parsed.Access;
    Method.Const = Parsed.Const;
    Method.RefQualified = Parsed.RefQualified;
    Method.Static = Parsed.Static;
    Method.Virtual = Parsed.Virtual;
    Method.Ctor = Parsed.Ctor;
    Method.Dtor = Parsed.Dtor;
    Method.Symbol = Symbol;
    Method.Rva = Rva;
    Method.Va = Va;
    Config.Methods.emplace(Method.Key, std::move(Method));
}

bool matchesClass(const std::string& Symbol, const std::string& ClassName) {
    return Symbol.find("@" + ClassName + "@@") != std::string::npos
        || Symbol.find("??0" + ClassName + "@@") != std::string::npos
        || Symbol.find("??1" + ClassName + "@@") != std::string::npos
        || Symbol.find("??_7" + ClassName + "@@") != std::string::npos
        || (Symbol.find("??_R") != std::string::npos && Symbol.find(ClassName) != std::string::npos);
}

// Extracts the owning class from an MSVC-mangled symbol without demangling.
// The result is a qualified name; template-mangled names are skipped because
// their class components are not stable identifiers.
std::string discoverMsvcClass(const std::string& Mangled) {
    if (Mangled.size() < 4 || Mangled[0] != '?') {
        return {};
    }
    if (Mangled.rfind("??_R", 0) == 0
        || Mangled.rfind("??__", 0) == 0) {
        return {};
    }
    size_t At = Mangled.find('@');
    if (At == std::string::npos) {
        return {};
    }
    // Constructors, destructors and vtables have no method name before the class, so the
    // class starts right after the two-character code. Every branch must run to the closing
    // "@@" rather than the first '@': stopping early keeps only the innermost component and
    // drops the namespace, which leaves namespaced classes with no symbols attached at all.
    size_t Start;
    if (Mangled.rfind("??0", 0) == 0
        || Mangled.rfind("??1", 0) == 0) {
        Start = 3;
    } else if (Mangled.rfind("??_G", 0) == 0
        || Mangled.rfind("??_E", 0) == 0
        || Mangled.rfind("??_D", 0) == 0
        || Mangled.rfind("??_7", 0) == 0) {
        Start = 4;
    } else {
        Start = At + 1;
    }
    std::string Class;
    {
        size_t DoubleAt = Mangled.find("@@", Start);
        if (DoubleAt == std::string::npos || DoubleAt <= Start) {
            return {};
        }
        Class = Mangled.substr(Start, DoubleAt - Start);
    }
    if (Class.empty()
        || Class.find('?') != std::string::npos
        || Class.find('$') != std::string::npos) {
        return {};
    }
    std::vector<std::string> Parts;
    std::string Part;
    for (char Character : Class) {
        if (Character == '@') {
            if (!Part.empty()) {
                Parts.push_back(Part);
            }
            Part.clear();
        } else {
            Part += Character;
        }
    }
    if (!Part.empty()) {
        Parts.push_back(Part);
    }
    std::reverse(Parts.begin(), Parts.end());

    std::string Qualified;
    for (const auto& Component : Parts) {
        if (!Qualified.empty()) {
            Qualified += "::";
        }
        Qualified += Component;
    }
    return Qualified;
}

// Extracts the owning class from an Itanium-mangled symbol. The trailing
// component is the member name; vtable and RTTI symbols use every component.
std::string discoverItaniumClass(const std::string& Mangled) {
    size_t Position = 0;
    bool Member = false;
    if (Mangled.rfind("_ZTV", 0) == 0
        || Mangled.rfind("_ZTI", 0) == 0
        || Mangled.rfind("_ZTS", 0) == 0) {
        Position = 4;
    } else if (Mangled.rfind("_ZNK", 0) == 0) {
        Position = 4;
        Member = true;
    } else if (Mangled.rfind("_ZN", 0) == 0) {
        Position = 3;
        Member = true;
    } else {
        return {};
    }
    if (Position < Mangled.size() && Mangled[Position] == 'N') {
        ++Position;
    }

    std::vector<std::string> Components;
    while (Position < Mangled.size()
        && std::isdigit(static_cast<unsigned char>(Mangled[Position]))) {
        size_t Length = 0;
        while (Position < Mangled.size()
            && std::isdigit(static_cast<unsigned char>(Mangled[Position]))) {
            Length = Length * 10 + static_cast<size_t>(Mangled[Position] - '0');
            ++Position;
        }
        if (Length == 0 || Position + Length > Mangled.size()) {
            return {};
        }
        Components.push_back(Mangled.substr(Position, Length));
        Position += Length;
    }
    if (Components.size() < (Member ? 2u : 1u)) {
        return {};
    }
    if (Member) {
        Components.pop_back();
    }

    std::string Qualified;
    for (const auto& Component : Components) {
        if (!Qualified.empty()) {
            Qualified += "::";
        }
        Qualified += Component;
    }
    if (Qualified.find('$') != std::string::npos
        || Qualified.find('<') != std::string::npos) {
        return {};
    }
    return Qualified;
}

uint64_t rvaForPublic(const object::COFFObjectFile& Coff, const codeview::PublicSym32& Public) {
    auto Section = Coff.getSection(Public.Segment);
    if (!Section) {
        return 0;
    }
    return (*Section)->VirtualAddress + Public.Offset;
}

bool readCoffBytes(const object::COFFObjectFile& Coff, uint64_t ImageBase, uint64_t Rva, void* Destination, size_t Size) {
    uintptr_t Pointer = 0;
    if (Error ReadError = Coff.getVaPtr(ImageBase + Rva, Pointer)) {
        consumeError(std::move(ReadError));
        return false;
    }
    std::memcpy(Destination, reinterpret_cast<const void*>(Pointer), Size);
    return true;
}

void mapVtableAddresses(Configuration& Config, const std::string& ClassName, uint64_t VtableAddress, bool Windows, uint64_t ImageBase, const object::COFFObjectFile* Coff, const object::ObjectFile* Elf) {
    const auto ClassMethods = Config.MethodsByClassAddress.find(ClassName);
    if (ClassMethods == Config.MethodsByClassAddress.end()) {
        return;
    }
    const auto& ByAddress = ClassMethods->second;

    if (!Windows) {
        for (size_t Slot = 2; Slot < 512; ++Slot) {
            auto Relocation = Config.ElfRelocations.find(VtableAddress + Slot * 8);
            if (Relocation == Config.ElfRelocations.end()) {
                continue;
            }
            auto Found = ByAddress.find(Relocation->second);
            if (Found == ByAddress.end()) {
                continue;
            }
            for (auto* Method : Found->second) {
                Method->VtableIndex = static_cast<int>(Slot - 2);
                Method->Virtual = true;
            }
        }
        return;
    }

    for (size_t Slot = 0; Slot < 512; ++Slot) {
        uint64_t Target = 0;
        if (!readCoffBytes(*Coff, ImageBase, VtableAddress + Slot * 8, &Target, sizeof(Target)) || Target == 0) {
            break;
        }
        auto Found = ByAddress.find(Target - ImageBase);
        if (Found == ByAddress.end()) {
            continue;
        }
        for (auto* Method : Found->second) {
            Method->VtableIndex = static_cast<int>(Slot);
            Method->Virtual = true;
        }
    }
}

bool loadPdb(Inputs& Inputs, const object::COFFObjectFile& Coff, Configuration& Config) {
    errs() << "loading PDB " << Inputs.Pdb << "\n";
    std::unique_ptr<pdb::IPDBSession> Session;
    if (Error LoadError = pdb::NativeSession::createFromPdbPath(Inputs.Pdb, Session)) {
        errs() << "cannot open PDB: " << toString(std::move(LoadError)) << "\n";
        return false;
    }
    auto* Native = static_cast<pdb::NativeSession*>(Session.get());
    errs() << "PDB session created\n";

    errs() << "opening symbol stream\n";
    auto& Symbols = cantFail(Native->getPDBFile().getPDBSymbolStream());
    bool HadError = false;
    uint64_t ImageBase = Coff.getImageBase();
    uint64_t Seen = 0;
    struct SupportCandidate {
        std::string ClassName;
        bool Destructor = false;
        bool Copy = false;
        std::string Symbol;
        uint64_t Rva = 0;
    };
    std::vector<SupportCandidate> SupportCandidates;

    for (auto& Symbol : Symbols.getSymbols(&HadError)) {
        ++Seen;
        if (Seen % 10000000 == 0) {
            errs() << "  " << Seen << " symbols\n";
        }
        if (Symbol.kind() != codeview::SymbolKind::S_PUB32) {
            continue;
        }
        auto Public = codeview::SymbolDeserializer::deserializeAs<codeview::PublicSym32>(Symbol);
        if (!Public) {
            consumeError(Public.takeError());
            continue;
        }

        std::string Mangled = Public->Name.str();
        std::string Discovered = discoverMsvcClass(Mangled);
        if (!Inputs.ListClasses.empty() && !Discovered.empty()) {
            ++Inputs.DiscoveredClasses[Discovered];
        }
        if (Mangled.size() > 1 && Mangled[0] == '?') {
            std::string StaticKey = parseMsvcStaticData(Mangled);
            if (!StaticKey.empty()) {
                std::string ClassName = StaticKey.substr(0, StaticKey.rfind("::"));
                if (Inputs.ClassSet.find(ClassName) != Inputs.ClassSet.end()) {
                    uint64_t Rva = rvaForPublic(Coff, *Public);
                    Config.StaticFields[StaticKey] = SupportSymbol{Mangled, Rva, ImageBase + Rva};
                }
            }
        }
        if (Mangled.rfind("??0", 0) == 0 || Mangled.rfind("??1", 0) == 0) {
            std::string Demangled = demangle(Mangled);
            std::string ClassName;
            bool IsDestructor = false;
            bool IsCopy = false;
            if (classifyRecordSupport(Demangled, true, ClassName, IsDestructor, IsCopy)) {
                SupportCandidates.push_back(
                    {ClassName, IsDestructor, IsCopy, Mangled, rvaForPublic(Coff, *Public)});
            }
        }
        if (Mangled == "??1?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@QEAA@XZ") {
            Config.StringDestructorSymbol = Mangled;
            Config.StringDestructorRva = rvaForPublic(Coff, *Public);
        }

        bool Wanted = !Discovered.empty()
            && Inputs.ClassSet.find(Discovered) != Inputs.ClassSet.end();
        if (!Wanted) {
            continue;
        }
        if (Mangled.find("??_7") == 0) {
            Config.VtableSymbols[Discovered] = Mangled;
            Config.VtableAddresses[Discovered] = rvaForPublic(Coff, *Public);
            continue;
        }
        if (Mangled.find("??_R") == 0) {
            continue;
        }

        std::string Demangled = demangle(Mangled);
        if (Demangled == Mangled || Demangled.find("[thunk]") != std::string::npos) {
            continue;
        }

        ParsedMethod Parsed = parseDemangled(Demangled, Discovered, true);
        if (Parsed.Valid) {
            uint64_t Rva = rvaForPublic(Coff, *Public);
            addMethod(Config, Parsed, Mangled, Rva, ImageBase + Rva);
        }
    }

    std::set<std::string> ReferencedTypes;
    for (const auto& [Key, Method] : Config.Methods) {
        addTypeTokens(Method.ReturnType, ReferencedTypes);
        for (const auto& Parameter : Method.Parameters) {
            addTypeTokens(Parameter, ReferencedTypes);
        }
    }
    for (const auto& Candidate : SupportCandidates) {
        if (ReferencedTypes.find(Candidate.ClassName) == ReferencedTypes.end()) {
            continue;
        }
        SupportSymbol Symbol{Candidate.Symbol, Candidate.Rva, ImageBase + Candidate.Rva};
        if (Candidate.Destructor) {
            Config.RecordDestructors[Candidate.ClassName] = Symbol;
        } else if (Candidate.Copy) {
            Config.RecordCopyConstructors[Candidate.ClassName] = Symbol;
        }
    }

    if (HadError) {
        errs() << "warning: PDB symbol stream reported an error\n";
    }
    return true;
}

bool readElfRaw(const object::ObjectFile& Elf, uint64_t Address, void* Destination, size_t Size) {
    for (auto Section : Elf.sections()) {
        uint64_t SectionAddress = Section.getAddress();
        uint64_t Length = Section.getSize();
        if (Address >= SectionAddress && Address + Size <= SectionAddress + Length) {
            auto Contents = Section.getContents();
            if (!Contents) {
                return false;
            }
            std::memcpy(Destination, Contents->data() + (Address - SectionAddress), Size);
            return true;
        }
    }
    return false;
}

uint64_t readElfPointer(const Configuration& Config, const object::ObjectFile& Elf, uint64_t Address) {
    auto Relocation = Config.ElfRelocations.find(Address);
    if (Relocation != Config.ElfRelocations.end()) {
        return Relocation->second;
    }
    uint64_t Value = 0;
    readElfRaw(Elf, Address, &Value, sizeof(Value));
    return Value;
}

template <typename T>
T readElfValue(const object::ObjectFile& Elf, uint64_t Address) {
    T Value{};
    readElfRaw(Elf, Address, &Value, sizeof(Value));
    return Value;
}

std::string readElfString(const object::ObjectFile& Elf, uint64_t Address) {
    std::string Result;
    for (auto Section : Elf.sections()) {
        uint64_t SectionAddress = Section.getAddress();
        uint64_t Length = Section.getSize();
        if (Address < SectionAddress || Address >= SectionAddress + Length) {
            continue;
        }
        auto Contents = Section.getContents();
        if (!Contents) {
            return Result;
        }
        const char* Data = Contents->data() + (Address - SectionAddress);
        size_t Available = static_cast<size_t>(Length - (Address - SectionAddress));
        for (size_t Index = 0; Index < Available && Index < 4096 && Data[Index] != 0; ++Index) {
            Result.push_back(static_cast<char>(Data[Index]));
        }
        return Result;
    }
    return Result;
}

std::string decodeItaniumTypeName(StringRef Text) {
    std::string Raw = Text.str();
    if (Text.consume_front("N")) {
        std::string Out;
        while (!Text.empty() && Text.front() != 'E') {
            if (Out.empty() && Text.consume_front("St")) {
                Out = "std";
                continue;
            }
            if (Text.consume_front("S")) {
                return Raw;
            }
            size_t Length = 0;
            size_t Digits = 0;
            while (!Text.empty() && std::isdigit(static_cast<unsigned char>(Text.front())) != 0) {
                Length = Length * 10 + static_cast<size_t>(Text.front() - '0');
                Text = Text.drop_front();
                ++Digits;
            }
            if (Digits == 0 || Text.size() < Length) {
                return Raw;
            }
            if (!Out.empty()) {
                Out += "::";
            }
            Out += Text.substr(0, Length).str();
            Text = Text.drop_front(Length);
        }
        return Out.empty() ? Raw : Out;
    }
    if (Text.consume_front("St")) {
        std::string Rest = decodeItaniumTypeName(Text);
        return Rest.empty() ? "std" : "std::" + Rest;
    }
    size_t Length = 0;
    size_t Digits = 0;
    while (!Text.empty() && std::isdigit(static_cast<unsigned char>(Text.front())) != 0) {
        Length = Length * 10 + static_cast<size_t>(Text.front() - '0');
        Text = Text.drop_front();
        ++Digits;
    }
    if (Digits == 0 || Text.size() < Length) {
        return Raw;
    }
    return Text.substr(0, Length).str();
}

void collectElfRtti(const Inputs& Inputs, const object::ObjectFile& Elf, Configuration& Config) {
    struct TypeInfo {
        std::string Name;
        std::vector<RttiBase> Bases;
    };
    std::map<uint64_t, TypeInfo> Cache;
    std::set<uint64_t> Visiting;

    std::function<TypeInfo(uint64_t)> readInfo = [&](uint64_t Address) -> TypeInfo {
        auto Cached = Cache.find(Address);
        if (Cached != Cache.end()) {
            return Cached->second;
        }
        if (!Visiting.insert(Address).second) {
            return TypeInfo{};
        }

        TypeInfo Info;
        uint64_t Vtable = readElfPointer(Config, Elf, Address);
        uint64_t NamePointer = readElfPointer(Config, Elf, Address + 8);
        Info.Name = decodeItaniumTypeName(readElfString(Elf, NamePointer));

        if (Vtable == Config.SiTypeInfoVtable) {
            uint64_t BaseAddress = readElfPointer(Config, Elf, Address + 16);
            TypeInfo Base = readInfo(BaseAddress);
            if (!Base.Name.empty()) {
                Info.Bases.push_back(RttiBase{Base.Name, 0, false, "public"});
                Config.RttiBases[Base.Name] = Base.Bases;
            }
        } else if (Vtable == Config.VmiTypeInfoVtable) {
            uint32_t Flags = readElfValue<uint32_t>(Elf, Address + 16);
            (void)Flags;
            uint32_t BaseCount = readElfValue<uint32_t>(Elf, Address + 20);
            for (uint32_t Index = 0; Index < BaseCount; ++Index) {
                uint64_t Entry = Address + 24 + Index * 16;
                uint64_t BaseAddress = readElfPointer(Config, Elf, Entry);
                uint64_t OffsetFlags = readElfValue<uint64_t>(Elf, Entry + 8);
                TypeInfo Base = readInfo(BaseAddress);
                if (Base.Name.empty()) {
                    continue;
                }
                int64_t Offset = static_cast<int64_t>(OffsetFlags) >> 8;
                bool Virtual = (OffsetFlags & 1) != 0;
                std::string Access = (OffsetFlags & 2) != 0 ? "public" : "private";
                Info.Bases.push_back(RttiBase{Base.Name, Offset, Virtual, Access});
                Config.RttiBases[Base.Name] = Base.Bases;
            }
        }

        Visiting.erase(Address);
        Config.RttiBases[Info.Name] = Info.Bases;
        Cache.emplace(Address, Info);
        return Info;
    };

    for (const auto& ClassName : Inputs.Classes) {
        std::string EncodedName = std::to_string(ClassName.size()) + ClassName;
        auto TypeInfo = Config.TypeInfoByName.find(EncodedName);
        if (TypeInfo != Config.TypeInfoByName.end()) {
            readInfo(TypeInfo->second);
        }
    }
}

bool loadElf(const Inputs& Inputs, const object::ObjectFile& Elf, Configuration& Config) {
    if (auto* ElfObject = dyn_cast<object::ELFObjectFileBase>(&Elf)) {
        for (auto Section : ElfObject->dynamic_relocation_sections()) {
            for (auto Relocation : Section.relocations()) {
                object::ELFRelocationRef ElfRelocation(Relocation);
                if (ElfRelocation.getType() != ELF::R_X86_64_RELATIVE) {
                    continue;
                }
                auto Addend = ElfRelocation.getAddend();
                if (!Addend) {
                    consumeError(Addend.takeError());
                    continue;
                }
                Config.ElfRelocations[ElfRelocation.getOffset()] = static_cast<uint64_t>(*Addend);
            }
        }
    }

    struct SupportCandidate {
        std::string ClassName;
        bool Destructor = false;
        bool Copy = false;
        std::string Symbol;
        uint64_t Address = 0;
    };
    std::vector<SupportCandidate> SupportCandidates;

    for (auto Symbol : Elf.symbols()) {
        auto Name = Symbol.getName();
        if (!Name) {
            consumeError(Name.takeError());
            continue;
        }
        std::string Mangled = Name->str();
        std::string Discovered = discoverItaniumClass(Mangled);

        auto SymbolValueResult = Symbol.getValue();
        if (!SymbolValueResult) {
            consumeError(SymbolValueResult.takeError());
            continue;
        }
        uint64_t SymbolValue = *SymbolValueResult;
        if (!Discovered.empty()
            && Inputs.ClassSet.find(Discovered) != Inputs.ClassSet.end()) {
            std::string Field;
            if (parseItaniumStaticData(Mangled, Discovered, Field)) {
                Config.StaticFields[Discovered + "::" + Field] =
                    SupportSymbol{Mangled, 0, SymbolValue};
            }
        }
        if (Mangled.rfind("_ZN", 0) == 0
            && (Mangled.find("C1E") != std::string::npos
                || Mangled.find("C2E") != std::string::npos
                || Mangled.find("D1E") != std::string::npos
                || Mangled.find("D2E") != std::string::npos)) {
            std::string Demangled = demangle(Mangled);
            std::string ClassName;
            bool IsDestructor = false;
            bool IsCopy = false;
            if (classifyRecordSupport(Demangled, false, ClassName, IsDestructor, IsCopy)) {
                SupportCandidates.push_back(
                    {ClassName, IsDestructor, IsCopy, Mangled, SymbolValue});
            }
        }
        if (Mangled == "_ZNSt3__112basic_stringIcNS_11char_traitsIcEENS_9allocatorIcEEED1Ev"
            || Mangled == "_ZNSsD1Ev") {
            Config.StringDestructorSymbol = Mangled;
            Config.StringDestructorAddress = SymbolValue;
        }
        if (Mangled == "_ZTVN10__cxxabiv117__class_type_infoE") {
            Config.ClassTypeInfoVtable = SymbolValue + 16;
        } else if (Mangled == "_ZTVN10__cxxabiv120__si_class_type_infoE") {
            Config.SiTypeInfoVtable = SymbolValue + 16;
        } else if (Mangled == "_ZTVN10__cxxabiv121__vmi_class_type_infoE") {
            Config.VmiTypeInfoVtable = SymbolValue + 16;
        }
        if (Mangled.rfind("_ZTI", 0) == 0) {
            Config.TypeInfoByName[Mangled.substr(4)] = SymbolValue;
        }

        bool Wanted = !Discovered.empty()
            && Inputs.ClassSet.find(Discovered) != Inputs.ClassSet.end();
        if (!Wanted) {
            continue;
        }

        if (Mangled.find("_ZTV") == 0) {
            Config.VtableSymbols[Discovered] = Mangled;
            Config.VtableAddresses[Discovered] = SymbolValue;
            continue;
        }

        std::string Demangled = demangle(Mangled);
        if (Demangled == Mangled || Demangled.find("[clone") != std::string::npos) {
            continue;
        }

        ParsedMethod Parsed = parseDemangled(Demangled, Discovered, false);
        if (Parsed.Valid) {
            uint64_t Address = SymbolValue;
            addMethod(Config, Parsed, Mangled, Address, Address);
        }
    }

    std::set<std::string> ReferencedTypes;
    for (const auto& [Key, Method] : Config.Methods) {
        addTypeTokens(Method.ReturnType, ReferencedTypes);
        for (const auto& Parameter : Method.Parameters) {
            addTypeTokens(Parameter, ReferencedTypes);
        }
    }
    for (const auto& Candidate : SupportCandidates) {
        if (ReferencedTypes.find(Candidate.ClassName) == ReferencedTypes.end()) {
            continue;
        }
        SupportSymbol Symbol{Candidate.Symbol, 0, Candidate.Address};
        if (Candidate.Destructor) {
            Config.RecordDestructors[Candidate.ClassName] = Symbol;
        } else if (Candidate.Copy) {
            Config.RecordCopyConstructors[Candidate.ClassName] = Symbol;
        }
    }

    collectElfRtti(Inputs, Elf, Config);
    return true;
}

std::string hex(uint64_t Value) {
    std::string Text;
    raw_string_ostream Stream(Text);
    Stream << format_hex(Value, 0, false);
    return Text;
}

json::Object buildMethodJson(const std::string& Key, const Configuration& Windows, const Configuration& Linux) {
    const Method* Win = nullptr;
    const Method* Lin = nullptr;
    auto WinIt = Windows.Methods.find(Key);
    if (WinIt != Windows.Methods.end()) {
        Win = &WinIt->second;
    }
    auto LinIt = Linux.Methods.find(Key);
    if (LinIt != Linux.Methods.end()) {
        Lin = &LinIt->second;
    }
    const Method& Base = Win != nullptr ? *Win : *Lin;

    json::Object Object;
    Object["key"] = Key;
    Object["name"] = Base.Name;
    Object["qualifiedName"] = Base.ClassName + "::" + Base.Name;
    Object["access"] = Win != nullptr ? Win->Access : "unknown";
    Object["const"] = Base.Const;
    Object["static"] = Win != nullptr ? Win->Static : false;
    Object["virtual"] = (Win != nullptr && Win->Virtual) || (Lin != nullptr && Lin->Virtual);
    Object["constructor"] = Base.Ctor;
    Object["destructor"] = Base.Dtor;

    json::Array Parameters;
    for (const auto& Parameter : Base.Parameters) {
        Parameters.emplace_back(Parameter);
    }
    Object["parameters"] = std::move(Parameters);

    std::string ReturnType = Win != nullptr ? Win->ReturnType : std::string();
    if (ReturnType.empty() && Lin != nullptr) {
        ReturnType = Lin->ReturnType;
    }
    if (!ReturnType.empty()) {
        Object["returnType"] = ReturnType;
    }

    auto addSide = [&](const char* Name, const Method* MethodInfo) {
        if (MethodInfo == nullptr) {
            return;
        }
        json::Object Side;
        Side["symbol"] = MethodInfo->Symbol;
        Side["address"] = hex(MethodInfo->Va);
        if (MethodInfo->Rva != MethodInfo->Va) {
            Side["rva"] = hex(MethodInfo->Rva);
        }
        if (!MethodInfo->ReturnType.empty()) {
            Side["returnType"] = MethodInfo->ReturnType;
        }
        if (MethodInfo->VtableIndex >= 0) {
            Side["vtableIndex"] = MethodInfo->VtableIndex;
        }
        Object[Name] = std::move(Side);
    };
    addSide("win-x64-server", Win);
    addSide("linux-x64-server", Lin);
    return Object;
}

struct MergedMethod {
    std::string Key;
    const Method* Win = nullptr;
    const Method* Lin = nullptr;
    std::string ReturnType;
    std::string Access = "unknown";
    bool Virtual = false;
    bool Static = false;
    bool Ctor = false;
    bool Dtor = false;
};

std::vector<MergedMethod> mergeMethods(const Configuration& Windows, const Configuration& Linux, const std::string& ClassName) {
    std::set<std::string> Keys;
    if (const auto Found = Windows.MethodKeysByClass.find(ClassName); Found != Windows.MethodKeysByClass.end()) {
        Keys.insert(Found->second.begin(), Found->second.end());
    }
    if (const auto Found = Linux.MethodKeysByClass.find(ClassName); Found != Linux.MethodKeysByClass.end()) {
        Keys.insert(Found->second.begin(), Found->second.end());
    }

    std::vector<MergedMethod> Result;
    for (const auto& Key : Keys) {
        MergedMethod Merged;
        Merged.Key = Key;
        auto WinIt = Windows.Methods.find(Key);
        if (WinIt != Windows.Methods.end()) {
            Merged.Win = &WinIt->second;
        }
        auto LinIt = Linux.Methods.find(Key);
        if (LinIt != Linux.Methods.end()) {
            Merged.Lin = &LinIt->second;
        }
        const Method& Base = Merged.Win != nullptr ? *Merged.Win : *Merged.Lin;
        Merged.ReturnType = Merged.Win != nullptr ? Merged.Win->ReturnType : Merged.Lin->ReturnType;
        Merged.Access = Merged.Win != nullptr ? Merged.Win->Access : Merged.Lin->Access;
        Merged.Virtual = (Merged.Win != nullptr && Merged.Win->Virtual) || (Merged.Lin != nullptr && Merged.Lin->Virtual);
        Merged.Static = Merged.Win != nullptr && Merged.Win->Static;
        Merged.Ctor = Base.Ctor;
        Merged.Dtor = Base.Dtor;
        Result.push_back(std::move(Merged));
    }
    return Result;
}

std::string effectiveReturnType(const MergedMethod& Method) {
    if (Method.Ctor || Method.Dtor) {
        return "void";
    }
    return Method.ReturnType.empty() ? "unknown" : Method.ReturnType;
}

std::string methodKind(const MergedMethod& Method) {
    if (Method.Ctor) {
        return "constructor";
    }
    if (Method.Dtor) {
        return "destructor";
    }
    if (startsWith(Method.Key.substr(Method.Key.find("::") + 2), "operator")) {
        return "operator";
    }
    return "method";
}

std::string memberId(const std::string& ClassName, const MergedMethod& MergedMethodInfo) {
    const Method& Base = MergedMethodInfo.Win != nullptr ? *MergedMethodInfo.Win : *MergedMethodInfo.Lin;
    std::string Signature = effectiveReturnType(MergedMethodInfo) + "(";
    for (const auto& Parameter : Base.Parameters) {
        Signature += Parameter;
        Signature += ",";
    }
    Signature += ")";
    return "bedrock:" + ClassName + "@" + ClassName + "::" + Base.Name + "#" + Signature
        + (Base.Const ? " const" : "");
}

json::Object makeMemberRecord(
    const std::string& ClassName,
    const std::string& DeclarationId,
    const std::string& ConfigurationId,
    const MergedMethod& Merged,
    const Method& MethodInfo) {
    json::Object Record;
    Record["recordType"] = "member";
    Record["id"] = memberId(ClassName, Merged);
    Record["declarationId"] = DeclarationId;
    Record["configuration"] = ConfigurationId;
    Record["kind"] = methodKind(Merged);
    Record["name"] = MethodInfo.Name;
    Record["access"] = Merged.Access;
    Record["static"] = Merged.Static;
    Record["virtual"] = Merged.Virtual;
    Record["pure"] = false;
    Record["inline"] = false;
    Record["defined"] = true;
    Record["consteval"] = false;
    if (MethodInfo.VtableIndex >= 0) {
        Record["vtableIndex"] = MethodInfo.VtableIndex;
    }
    Record["symbol"] = MethodInfo.Symbol;
    Record["address"] = MethodInfo.Va;
    if (MethodInfo.Rva != MethodInfo.Va) {
        Record["rva"] = MethodInfo.Rva;
    }

    json::Object Signature;
    Signature["canonical"] = ClassName + "::" + MethodInfo.Name;
    Signature["returnType"] = effectiveReturnType(Merged);
    json::Array Parameters;
    for (size_t Index = 0; Index < MethodInfo.Parameters.size(); ++Index) {
        json::Object Parameter;
        Parameter["name"] = "arg" + std::to_string(Index);
        Parameter["type"] = MethodInfo.Parameters[Index];
        Parameters.emplace_back(std::move(Parameter));
    }
    Signature["parameters"] = std::move(Parameters);
    Signature["variadic"] = false;
    Signature["const"] = MethodInfo.Const;
    Record["signature"] = std::move(Signature);
    return Record;
}

std::string sha256Text(StringRef Text) {
    auto Digest = SHA256::hash(ArrayRef<uint8_t>(
        reinterpret_cast<const uint8_t*>(Text.data()), Text.size()));
    return "sha256:" + toHex(ArrayRef<uint8_t>(Digest.data(), Digest.size()));
}

std::string recordKey(const json::Object& Record) {
    if (auto* Id = Record.get("id")) {
        if (auto Text = Id->getAsString()) {
            return Text->str();
        }
    }
    if (auto* DeclarationId = Record.get("declarationId")) {
        if (auto Text = DeclarationId->getAsString()) {
            return Text->str();
        }
    }
    return "";
}

std::string serializeRecords(std::vector<json::Object>& Records) {
    std::sort(Records.begin(), Records.end(), [](const json::Object& Left, const json::Object& Right) {
        return recordKey(Left) < recordKey(Right);
    });
    std::string Text;
    raw_string_ostream Stream(Text);
    for (auto& Record : Records) {
        Stream << json::Value(std::move(Record)) << "\n";
    }
    Stream.flush();
    return Text;
}

bool writeFile(const std::filesystem::path& Path, StringRef Text) {
    std::error_code Error;
    raw_fd_ostream Output(Path.string(), Error);
    if (Error) {
        errs() << "cannot write " << Path.string() << ": " << Error.message() << "\n";
        return false;
    }
    Output << Text;
    return true;
}

bool emitIr(const Inputs& Inputs, const Configuration& Windows, const Configuration& Linux, uint64_t ImageBase, bool HasLinux) {
    std::filesystem::create_directories(Inputs.IrOutput);
    const auto OutputDirectory = std::filesystem::path(Inputs.IrOutput);
    if (Inputs.Verbose) {
        errs() << "emitting " << Inputs.Classes.size() << " classes\n";
    }

    std::vector<json::Object> WinRecords;
    std::vector<json::Object> LinuxRecords;
    int64_t WinMembers = 0;
    int64_t LinuxMembers = 0;

    std::set<std::string> DeclarationIds;
    for (const auto& ClassName : Inputs.Classes) {
        DeclarationIds.insert("bedrock:" + ClassName);
    }
    for (const Configuration* Config : {&Windows, &Linux}) {
        for (const auto& [Name, Bases] : Config->RttiBases) {
            for (const auto& Base : Bases) {
                const std::string DeclarationId = "bedrock:" + Base.Name;
                if (!DeclarationIds.insert(DeclarationId).second) {
                    continue;
                }
                json::Object Declaration;
                Declaration["recordType"] = "declaration";
                Declaration["id"] = DeclarationId;
                Declaration["surface"] = "bedrock";
                Declaration["kind"] = "class";
                Declaration["qualifiedName"] = Base.Name;
                Declaration["access"] = "public";
                Declaration["linkage"] = "external";
                Declaration["abstract"] = false;
                Declaration["copyConstructible"] = false;
                Declaration["inline"] = false;
                Declaration["defined"] = false;
                Declaration["consteval"] = false;
                WinRecords.push_back(std::move(Declaration));
            }
        }
    }

    size_t ClassIndex = 0;
    for (const auto& ClassName : Inputs.Classes) {
        ++ClassIndex;
        if (Inputs.Verbose && (ClassIndex % 100 == 0 || ClassIndex == Inputs.Classes.size())) {
            errs() << "  " << ClassIndex << "/" << Inputs.Classes.size() << " " << ClassName << "\n";
        }
        const std::string DeclarationId = "bedrock:" + ClassName;
        json::Object Declaration;
        Declaration["recordType"] = "declaration";
        Declaration["id"] = DeclarationId;
        Declaration["surface"] = "bedrock";
        Declaration["kind"] = "class";
        Declaration["qualifiedName"] = ClassName;
        Declaration["access"] = "public";
        Declaration["linkage"] = "external";
        Declaration["abstract"] = false;
        Declaration["copyConstructible"] = false;
        Declaration["inline"] = false;
        Declaration["defined"] = false;
        Declaration["consteval"] = false;
        WinRecords.push_back(std::move(Declaration));

        std::vector<std::pair<const char*, const Configuration*>> Configs{
            { "win-x64-server", &Windows }};
        if (HasLinux) {
            Configs.emplace_back("linux-x64-server", &Linux);
        }
        for (const auto& [ConfigId, Config] : Configs) {
            json::Object Variant;
            Variant["recordType"] = "variant";
            Variant["declarationId"] = DeclarationId;
            Variant["configuration"] = ConfigId;
            Variant["availability"] = "present";
            auto Vtable = Config->VtableSymbols.find(ClassName);
            if (Vtable != Config->VtableSymbols.end()) {
                uint64_t Address = Config->VtableAddresses.at(ClassName);
                Variant["symbol"] = Vtable->second;
                if (Config == &Windows) {
                    Variant["address"] = Address + ImageBase;
                    Variant["rva"] = Address;
                } else {
                    Variant["address"] = Address;
                }
            }
            auto RttiBases = Config->RttiBases.find(ClassName);
            if (RttiBases != Config->RttiBases.end() && !RttiBases->second.empty()) {
                json::Array Bases;
                for (const auto& Base : RttiBases->second) {
                    json::Object BaseRecord;
                    BaseRecord["declarationId"] = "bedrock:" + Base.Name;
                    BaseRecord["offset"] = Base.Offset;
                    BaseRecord["access"] = Base.Access;
                    BaseRecord["virtual"] = Base.Virtual;
                    Bases.emplace_back(std::move(BaseRecord));
                }
                Variant["bases"] = std::move(Bases);
            }
            (Config == &Windows ? WinRecords : LinuxRecords).push_back(std::move(Variant));
        }

        auto Merged = mergeMethods(Windows, Linux, ClassName);
        for (const auto& Method : Merged) {
            if (Method.Win != nullptr) {
                WinRecords.push_back(makeMemberRecord(ClassName, DeclarationId, "win-x64-server", Method, *Method.Win));
                ++WinMembers;
            }
            if (Method.Lin != nullptr) {
                LinuxRecords.push_back(makeMemberRecord(ClassName, DeclarationId, "linux-x64-server", Method, *Method.Lin));
                ++LinuxMembers;
            }
        }
    }

    const int64_t WinRecordCount = static_cast<int64_t>(WinRecords.size());
    const int64_t LinuxRecordCount = static_cast<int64_t>(LinuxRecords.size());
    const std::string WinText = serializeRecords(WinRecords);
    const std::string LinuxText = HasLinux ? serializeRecords(LinuxRecords) : std::string();
    if (!writeFile(OutputDirectory / "win-x64-server.jsonl", WinText)
        || (HasLinux && !writeFile(OutputDirectory / "linux-x64-server.jsonl", LinuxText))) {
        return false;
    }

    json::Object Manifest;
    Manifest["schema"] = "hosihikari.bindings.ir/1";
    Manifest["irVersion"] = "1.6.0";
    json::Object Frontend;
    Frontend["name"] = "symbols";
    Frontend["version"] = "0.1.0";
    Frontend["llvm"] = "23.1.0";
    Manifest["frontend"] = std::move(Frontend);

    json::Array Sources;
    std::vector<std::string> SourcePaths{Inputs.Pdb, Inputs.Exe};
    if (!Inputs.Elf.empty()) {
        SourcePaths.push_back(Inputs.Elf);
    }
    for (const auto& Source : SourcePaths) {
        json::Object Pin;
        Pin["surface"] = "bedrock";
        Pin["repo"] = "local";
        Pin["commit"] = Inputs.Version;
        Pin["path"] = Source;
        Sources.emplace_back(std::move(Pin));
    }
    Manifest["sources"] = std::move(Sources);

    const std::string EmptyHash = sha256Text("");
    json::Array Configurations;
    std::vector<std::pair<const char*, const char*>> ConfigIds{{ "win-x64-server", "windows" }};
    if (HasLinux) {
        ConfigIds.emplace_back("linux-x64-server", "linux");
    }
    for (const auto& [Id, Os] : ConfigIds) {
        json::Object Configuration;
        Configuration["id"] = Id;
        Configuration["os"] = Os;
        Configuration["arch"] = "x64";
        Configuration["target"] = "server";
        Configuration["defines"] = json::Array();
        Configuration["definesHash"] = EmptyHash;
        Configurations.emplace_back(std::move(Configuration));
    }
    Manifest["configurations"] = std::move(Configurations);

    json::Array Shards;
    std::vector<std::tuple<const char*, const char*, const std::string*, int64_t>> ShardInputs{
        { "win-x64-server", "win-x64-server.jsonl", &WinText, WinRecordCount }};
    if (HasLinux) {
        ShardInputs.emplace_back("linux-x64-server", "linux-x64-server.jsonl", &LinuxText, LinuxRecordCount);
    }
    for (const auto& [Id, Path, Text, RecordCount] : ShardInputs) {
        json::Object Shard;
        Shard["surface"] = "bedrock";
        Shard["configuration"] = Id;
        Shard["module"] = "bedrock";
        Shard["path"] = Path;
        Shard["sha256"] = sha256Text(*Text);
        Shard["sha256Compressed"] = sha256Text(*Text);
        Shard["records"] = RecordCount;
        Shards.emplace_back(std::move(Shard));
    }
    Manifest["shards"] = std::move(Shards);

    json::Object Diagnostics;
    Diagnostics["errors"] = 0;
    Diagnostics["warnings"] = 0;
    Diagnostics["shardsWithErrors"] = json::Array();
    Manifest["diagnostics"] = std::move(Diagnostics);

    json::Object SupportSymbols;
    json::Object StringDestructorConfigs;
    if (Windows.StringDestructorRva != 0) {
        json::Object Entry;
        Entry["symbol"] = Windows.StringDestructorSymbol;
        Entry["rva"] = Windows.StringDestructorRva;
        Entry["address"] = ImageBase + Windows.StringDestructorRva;
        StringDestructorConfigs["win-x64-server"] = std::move(Entry);
    }
    if (HasLinux && Linux.StringDestructorAddress != 0) {
        json::Object Entry;
        Entry["symbol"] = Linux.StringDestructorSymbol;
        Entry["address"] = Linux.StringDestructorAddress;
        StringDestructorConfigs["linux-x64-server"] = std::move(Entry);
    }
    SupportSymbols["stdStringDestructor"] = std::move(StringDestructorConfigs);
    Manifest["supportSymbols"] = std::move(SupportSymbols);

    std::set<std::string> RecordSupportNames;
    for (const auto& [Name, Symbol] : Windows.RecordDestructors) {
        RecordSupportNames.insert(Name);
    }
    for (const auto& [Name, Symbol] : Windows.RecordCopyConstructors) {
        RecordSupportNames.insert(Name);
    }
    for (const auto& [Name, Symbol] : Linux.RecordDestructors) {
        RecordSupportNames.insert(Name);
    }
    for (const auto& [Name, Symbol] : Linux.RecordCopyConstructors) {
        RecordSupportNames.insert(Name);
    }

    json::Object RecordSupportJson;
    for (const auto& Name : RecordSupportNames) {
        json::Object Configs;
        auto addConfig = [&](const char* ConfigId, const Configuration& Config) {
            auto Destructor = Config.RecordDestructors.find(Name);
            auto Copy = Config.RecordCopyConstructors.find(Name);
            if (Destructor == Config.RecordDestructors.end()
                && Copy == Config.RecordCopyConstructors.end()) {
                return;
            }

            json::Object Entry;
            if (Destructor != Config.RecordDestructors.end()) {
                json::Object Value;
                Value["symbol"] = Destructor->second.Symbol;
                if (Destructor->second.Rva != 0) {
                    Value["rva"] = Destructor->second.Rva;
                }
                Value["address"] = Destructor->second.Address;
                Entry["destructor"] = std::move(Value);
            }
            if (Copy != Config.RecordCopyConstructors.end()) {
                json::Object Value;
                Value["symbol"] = Copy->second.Symbol;
                if (Copy->second.Rva != 0) {
                    Value["rva"] = Copy->second.Rva;
                }
                Value["address"] = Copy->second.Address;
                Entry["copyConstructor"] = std::move(Value);
            }

            Configs[ConfigId] = std::move(Entry);
        };
        addConfig("win-x64-server", Windows);
        if (HasLinux) {
            addConfig("linux-x64-server", Linux);
        }
        if (!Configs.empty()) {
            RecordSupportJson[Name] = std::move(Configs);
        }
    }
    Manifest["recordSupport"] = std::move(RecordSupportJson);

    std::set<std::string> StaticFieldNames;
    for (const auto& [Name, Symbol] : Windows.StaticFields) {
        StaticFieldNames.insert(Name);
    }
    for (const auto& [Name, Symbol] : Linux.StaticFields) {
        StaticFieldNames.insert(Name);
    }
    json::Object StaticFieldsJson;
    for (const auto& Name : StaticFieldNames) {
        json::Object Configs;
        auto addConfig = [&](const char* ConfigId, const Configuration& Config) {
            auto Symbol = Config.StaticFields.find(Name);
            if (Symbol == Config.StaticFields.end()) {
                return;
            }
            json::Object Entry;
            Entry["symbol"] = Symbol->second.Symbol;
            if (Symbol->second.Rva != 0) {
                Entry["rva"] = Symbol->second.Rva;
            }
            Entry["address"] = Symbol->second.Address;
            Configs[ConfigId] = std::move(Entry);
        };
        addConfig("win-x64-server", Windows);
        if (HasLinux) {
            addConfig("linux-x64-server", Linux);
        }
        if (!Configs.empty()) {
            StaticFieldsJson[Name] = std::move(Configs);
        }
    }
    Manifest["staticFields"] = std::move(StaticFieldsJson);

    json::Object Counts;
    Counts["declarations"] = static_cast<int64_t>(DeclarationIds.size());
    Counts["variants"] = static_cast<int64_t>(Inputs.Classes.size() * (HasLinux ? 2 : 1));
    Counts["members"] = WinMembers + (HasLinux ? LinuxMembers : 0);
    Manifest["counts"] = std::move(Counts);

    std::string ManifestText;
    raw_string_ostream ManifestStream(ManifestText);
    ManifestStream << json::Value(std::move(Manifest)) << "\n";
    ManifestStream.flush();
    return writeFile(OutputDirectory / "ir-manifest.json", ManifestText);
}

} // namespace

int main(int argc, char** argv) {
    Inputs Inputs;
    for (int Index = 1; Index < argc; ++Index) {
        StringRef Argument = argv[Index];
        auto next = [&]() -> std::string {
            if (Index + 1 >= argc) {
                errs() << "missing value for " << Argument << "\n";
                std::exit(2);
            }
            return argv[++Index];
        };
        if (Argument == "--pdb") {
            Inputs.Pdb = next();
        } else if (Argument == "--exe") {
            Inputs.Exe = next();
        } else if (Argument == "--elf") {
            Inputs.Elf = next();
        } else if (Argument == "--output") {
            Inputs.Output = next();
        } else if (Argument == "--ir-output") {
            Inputs.IrOutput = next();
        } else if (Argument == "--version") {
            Inputs.Version = next();
        } else if (Argument == "--classes") {
            std::string Classes = next();
            size_t Start = 0;
            while (Start <= Classes.size()) {
                size_t Comma = Classes.find(',', Start);
                std::string Name = trim(StringRef(Classes).slice(Start, Comma));
                if (!Name.empty()) {
                    Inputs.Classes.push_back(Name);
                }
                if (Comma == std::string::npos) {
                    break;
                }
                Start = Comma + 1;
            }
        } else if (Argument == "--classes-file") {
            std::ifstream ClassFile(next());
            if (!ClassFile) {
                errs() << "cannot read classes file\n";
                return 2;
            }
            std::string Line;
            while (std::getline(ClassFile, Line)) {
                std::string Name = StringRef(Line).trim().str();
                if (!Name.empty() && Name[0] != '#') {
                    Inputs.Classes.push_back(Name);
                }
            }
        } else if (Argument == "--list-classes") {
            Inputs.ListClasses = next();
        } else if (Argument == "--verbose") {
            Inputs.Verbose = true;
        } else {
            errs() << "unknown argument: " << Argument << "\n";
            return 2;
        }
    }

    std::vector<std::string> UniqueClasses;
    std::set<std::string> SeenClasses;
    for (const auto& Name : Inputs.Classes) {
        if (SeenClasses.insert(Name).second) {
            UniqueClasses.push_back(Name);
        }
    }
    Inputs.Classes = std::move(UniqueClasses);
    for (const auto& Name : Inputs.Classes) {
        Inputs.ClassSet.insert(Name);
    }

    if (Inputs.Pdb.empty() || Inputs.Exe.empty()
        || (Inputs.ListClasses.empty()
            && ((Inputs.Output.empty() && Inputs.IrOutput.empty()) || Inputs.Classes.empty()))) {
        errs() << "usage: hosihikari-symbol-ir-frontend --pdb <pdb> --exe <pe> [--elf <debug>] "
                  "[--output <prototype.json>] [--ir-output <dir>] [--version <id>] "
                  "[--classes <A,B> | --classes-file <path>] [--list-classes <path>]\n";
        return 2;
    }

    auto ExeBinary = object::createBinary(Inputs.Exe);
    if (!ExeBinary) {
        errs() << "cannot read PE: " << toString(ExeBinary.takeError()) << "\n";
        return 1;
    }
    auto* Coff = dyn_cast<object::COFFObjectFile>(ExeBinary->getBinary());
    if (Coff == nullptr) {
        errs() << "input is not a COFF/PE image\n";
        return 1;
    }
    errs() << "PE loaded, image base " << format_hex(Coff->getImageBase(), 0, false) << "\n";

    std::optional<object::OwningBinary<object::Binary>> ElfBinary;
    const object::ObjectFile* Elf = nullptr;
    if (!Inputs.Elf.empty() && Inputs.ListClasses.empty()) {
        auto LoadedElf = object::createBinary(Inputs.Elf);
        if (!LoadedElf) {
            errs() << "cannot read ELF: " << toString(LoadedElf.takeError()) << "\n";
            return 1;
        }
        ElfBinary.emplace(std::move(*LoadedElf));
        Elf = dyn_cast<object::ObjectFile>(ElfBinary->getBinary());
        if (Elf == nullptr) {
            errs() << "input is not an object file\n";
            return 1;
        }
    }
    const bool HasLinux = Elf != nullptr;

    Configuration Windows;
    Windows.Id = "win-x64-server";
    Configuration Linux;
    Linux.Id = "linux-x64-server";

    errs() << "reading PDB configuration\n";
    if (!loadPdb(Inputs, *Coff, Windows)) {
        return 1;
    }

    if (!Inputs.ListClasses.empty()) {
        std::filesystem::path ListPath(Inputs.ListClasses);
        if (ListPath.has_parent_path()) {
            std::filesystem::create_directories(ListPath.parent_path());
        }

        std::vector<std::pair<std::string, uint64_t>> Rows(
            Inputs.DiscoveredClasses.begin(), Inputs.DiscoveredClasses.end());
        std::sort(Rows.begin(), Rows.end(), [](const auto& Left, const auto& Right) {
            return Left.second != Right.second ? Left.second > Right.second : Left.first < Right.first;
        });

        std::ofstream Out(Inputs.ListClasses);
        Out << "class\tsymbols\n";
        for (const auto& [Name, Count] : Rows) {
            Out << Name << '\t' << Count << '\n';
        }
        errs() << "discovered " << Rows.size() << " classes\n";
        return 0;
    }

    if (HasLinux) {
        errs() << "reading ELF configuration\n";
        if (!loadElf(Inputs, *Elf, Linux)) {
            return 1;
        }
    }

    for (auto& [ClassName, Symbol] : Windows.VtableSymbols) {
        mapVtableAddresses(Windows, ClassName, Windows.VtableAddresses[ClassName], true, Coff->getImageBase(), Coff, nullptr);
    }
    if (HasLinux) {
        for (auto& [ClassName, Symbol] : Linux.VtableSymbols) {
            mapVtableAddresses(Linux, ClassName, Linux.VtableAddresses[ClassName], false, 0, nullptr, Elf);
        }
    }

    for (Configuration* Config : {&Windows, &Linux}) {
        for (auto& [Key, Method] : Config->Methods) {
            Config->MethodKeysByClass[Method.ClassName].insert(Key);
            if (Method.Rva != 0) {
                Config->MethodsByClassAddress[Method.ClassName][Method.Rva].push_back(&Method);
            }
            if (Method.Va != 0 && Method.Va != Method.Rva) {
                Config->MethodsByClassAddress[Method.ClassName][Method.Va].push_back(&Method);
            }
        }
    }

    if (!Inputs.IrOutput.empty() && !emitIr(Inputs, Windows, Linux, Coff->getImageBase(), HasLinux)) {
        return 1;
    }
    if (Inputs.Output.empty()) {
        return 0;
    }

    json::Object Root;
    Root["prototype"] = "hosihikari.bedrock.symbols/1";
    Root["version"] = Inputs.Version;
    json::Object InputFiles;
    InputFiles["pdb"] = Inputs.Pdb;
    InputFiles["exe"] = Inputs.Exe;
    InputFiles["elf"] = Inputs.Elf;
    Root["inputs"] = std::move(InputFiles);

    json::Array Classes;
    for (const auto& ClassName : Inputs.Classes) {
        json::Object ClassObject;
        ClassObject["name"] = ClassName;

        json::Object Vtables;
        auto addVtable = [&](const char* ConfigId, Configuration& Config) {
            auto Symbol = Config.VtableSymbols.find(ClassName);
            if (Symbol == Config.VtableSymbols.end()) {
                return;
            }
            json::Object Vtable;
            Vtable["symbol"] = Symbol->second;
            Vtable["address"] = hex(Config.VtableAddresses[ClassName]);
            Vtables[ConfigId] = std::move(Vtable);
        };
        addVtable("win-x64-server", Windows);
        addVtable("linux-x64-server", Linux);
        if (!Vtables.empty()) {
            ClassObject["vtables"] = std::move(Vtables);
        }

        std::set<std::string> Keys;
        if (const auto Found = Windows.MethodKeysByClass.find(ClassName); Found != Windows.MethodKeysByClass.end()) {
            Keys.insert(Found->second.begin(), Found->second.end());
        }
        if (const auto Found = Linux.MethodKeysByClass.find(ClassName); Found != Linux.MethodKeysByClass.end()) {
            Keys.insert(Found->second.begin(), Found->second.end());
        }

        json::Array Methods;
        for (const auto& Key : Keys) {
            Methods.emplace_back(buildMethodJson(Key, Windows, Linux));
        }
        ClassObject["methods"] = std::move(Methods);
        ClassObject["winMethodCount"] = static_cast<int64_t>(std::count_if(
            Windows.Methods.begin(), Windows.Methods.end(),
            [&](const auto& Entry) { return Entry.second.ClassName == ClassName; }));
        ClassObject["linuxMethodCount"] = static_cast<int64_t>(std::count_if(
            Linux.Methods.begin(), Linux.Methods.end(),
            [&](const auto& Entry) { return Entry.second.ClassName == ClassName; }));
        Classes.emplace_back(std::move(ClassObject));
    }
    Root["classes"] = std::move(Classes);

    std::error_code Error;
    raw_fd_ostream OutStream{Inputs.Output, Error};
    if (Error) {
        errs() << "cannot write output: " << Error.message() << "\n";
        return 1;
    }
    OutStream << json::Value(std::move(Root)) << "\n";
    return 0;
}
