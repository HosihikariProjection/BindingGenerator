#!/usr/bin/env python3
"""Merge header-derived facts into symbol-derived IR 1.4.0.

Usage:
  python merge-symbol-ir.py --symbol <symbol-ir-dir> --header <header-ir-dir> --output <dir>
"""

import argparse
import copy
import hashlib
import json
import os
import re


def load_records(path):
    with open(path, encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def record_key(record):
    return record.get("id") or record.get("declarationId") or ""


# LeviLamina's header generator annotates every data member it recovered with the offset
# it read from the carrier's symbols (`// this+0x128`). A member list that lost entries
# still parses, so these annotations are the only record of whether the header is
# complete: clang lays the type out as if the missing members did not exist.
_THIS_OFFSET = re.compile(r"//\s*this\+0x([0-9A-Fa-f]+)\s*$")
_SOURCE_LINES = {}
SOURCE_UNAVAILABLE = object()


def _read_source(path, header_root):
    if path in _SOURCE_LINES:
        return _SOURCE_LINES[path]
    lines = None
    candidates = [path]
    if header_root:
        # The header IR records absolute source paths from the checkout it was built in.
        # Re-root them so an IR produced elsewhere still finds its annotations.
        marker = max(path.rfind("\\src\\"), path.rfind("/src/"))
        if marker >= 0:
            candidates.append(
                os.path.join(
                    header_root, "src", path[marker + 5 :].replace("/", os.sep)
                )
            )
    for candidate in candidates:
        try:
            with open(candidate, encoding="utf-8", errors="replace") as stream:
                lines = stream.read().splitlines()
            break
        except OSError:
            continue
    _SOURCE_LINES[path] = lines
    return lines


def source_member_offsets(source, header_root=None):
    """Byte offsets annotated on the data-member lines of a generated header.

    Returns None when the file carries no annotations -- the hand-written headers are
    complete definitions by construction -- and SOURCE_UNAVAILABLE when the file cannot be
    read at all, which proves nothing and must not be read as "complete".
    """
    path = (source or {}).get("file")
    if not path:
        # A declaration without a recorded source comes from a hand-authored IR (the test
        # fixtures); there is nothing to check and nothing that could have moved.
        return None
    line = (source or {}).get("line")
    if not isinstance(line, int) or line < 1:
        return None
    lines = _read_source(path, header_root)
    if not lines or line > len(lines):
        return SOURCE_UNAVAILABLE

    depth = 0
    started = False
    offsets = []
    for text in lines[line - 1 :]:
        code = text.split("//")[0]
        opens = code.count("{")
        depth += opens - code.count("}")
        if opens:
            started = True
        if not started:
            continue
        # Only the class's own scope: nested types carry their own declarations.
        if depth == 1:
            match = _THIS_OFFSET.search(text)
            if match:
                offsets.append(int(match.group(1), 16))
        if depth <= 0:
            break
    return offsets or None


def serialize(records):
    ordered = sorted(
        records, key=lambda record: (record_key(record), record.get("recordType", ""))
    )
    return "".join(
        json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
        + "\n"
        for record in ordered
    )


def sha256_text(text):
    return "sha256:" + hashlib.sha256(text.encode("utf-8")).hexdigest()


def normalize_signature(identifier):
    if "#" not in identifier:
        return identifier
    prefix, signature = identifier.split("#", 1)
    signature = signature.replace(" ", "")
    signature = signature.replace("class", "").replace("struct", "").replace("enum", "")
    signature = signature.replace("std::__1::", "std::").replace(
        "std::__cxx11::", "std::"
    )
    signature = signature.replace("Puv::Legacy::", "")
    signature = signature.replace("const", "").replace("volatile", "")
    signature = signature.replace("unsigned__int64", "unsignedlonglong")
    signature = signature.replace("__int64", "longlong")
    signature = signature.replace("unsignedlong", "unsignedlonglong")
    signature = signature.replace(
        "std::basic_string<char,std::char_traits<char>,std::allocator<char>>",
        "std::string",
    )
    signature = signature.replace(
        "std::basic_string<unsignedshort,std::char_traits<unsignedshort>,std::allocator<unsignedshort>>",
        "std::u16string",
    )
    # The header side spells the default template arguments away; the demangler keeps them.
    signature = signature.replace("std::basic_string<char>", "std::string")
    signature = signature.replace("std::basic_string_view<char>", "std::string_view")
    signature = signature.replace(
        "std::basic_string_view<unsignedshort>", "std::u16string_view"
    )
    # The demangler prints default allocator/delete arguments; clang canonical types omit them.
    while True:
        simplified = re.sub(r",std::allocator<[^<>]*>>", ">", signature)
        simplified = re.sub(r",std::allocator<[^<>]*>", "", simplified)
        simplified = re.sub(r",std::default_delete<[^<>]*>>", ">", simplified)
        simplified = re.sub(r",std::default_delete<[^<>]*>", "", simplified)
        if simplified == signature:
            break
        signature = simplified
    return prefix + "#" + signature


def find_shard(manifest, configuration):
    for shard in manifest.get("shards", []):
        if shard.get("configuration") == configuration:
            return shard
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--symbol", required=True)
    parser.add_argument("--header", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--classes", default="Actor,BlockPos")
    # A comma list stops working past a few thousand names: the Windows command line caps
    # at 32,767 characters and the class set keeps growing.
    parser.add_argument("--classes-file", default="")
    parser.add_argument(
        "--layout-trust",
        default=os.path.join(
            os.path.dirname(os.path.abspath(__file__)), "layout-trust.txt"
        ),
        help="Layouts to accept without the completeness check, one qualified name per line.",
    )
    parser.add_argument(
        "--class-sizes",
        default="",
        help="JSON map of qualified name to sizeof, as written by extract-class-sizes.py.",
    )
    parser.add_argument(
        "--header-root",
        default="",
        help="Checkout the header IR was built from, used to re-root recorded source paths.",
    )
    args = parser.parse_args()

    if args.classes_file:
        with open(args.classes_file, encoding="utf-8") as stream:
            classes = [
                line.strip()
                for line in stream
                if line.strip() and not line.strip().startswith("#")
            ]
    else:
        classes = [item.strip() for item in args.classes.split(",") if item.strip()]
    with open(
        os.path.join(args.symbol, "ir-manifest.json"), encoding="utf-8"
    ) as stream:
        symbol_manifest = json.load(stream)
    with open(
        os.path.join(args.header, "ir-manifest.json"), encoding="utf-8"
    ) as stream:
        header_manifest = json.load(stream)

    configurations = [
        configuration["id"]
        for configuration in symbol_manifest.get("configurations", [])
    ]
    symbol_records = {configuration: [] for configuration in configurations}
    for configuration in configurations:
        shard = find_shard(symbol_manifest, configuration)
        if shard is None:
            raise SystemExit(f"symbol manifest has no shard for {configuration}")
        symbol_records[configuration] = load_records(
            os.path.join(args.symbol, shard["path"])
        )

    # Existence checks below run once per class and once per header member. Scanning the
    # record list each time made the merge quadratic and took tens of minutes once the class
    # set passed three thousand, so keep the keys in sets and update them on append.
    variant_ids = {configuration: set() for configuration in configurations}
    member_ids = {configuration: set() for configuration in configurations}
    for configuration in configurations:
        for record in symbol_records[configuration]:
            if record.get("recordType") == "variant" and record.get("declarationId"):
                variant_ids[configuration].add(record["declarationId"])
            elif record.get("recordType") == "member" and record.get("id"):
                member_ids[configuration].add(record["id"])
    variant_by_id = {configuration: {} for configuration in configurations}
    for configuration in configurations:
        for record in symbol_records[configuration]:
            if record.get("recordType") == "variant" and record.get("declarationId"):
                variant_by_id[configuration][record["declarationId"]] = record

    # The Itanium RTTI walk leaves template base names in their encoded form when a
    # template argument is not a simple length-prefixed component. Those names are not
    # usable C++ class names, so drop their declarations and base references.
    mangled_declaration = re.compile(r"^bedrock:(?:N\d|NSt|_Z)")
    for configuration in configurations:
        records = [
            record
            for record in symbol_records[configuration]
            if not (
                record.get("recordType") == "declaration"
                and mangled_declaration.match(record.get("id", ""))
            )
        ]
        for record in records:
            if record.get("recordType") == "variant" and record.get("bases"):
                record["bases"] = [
                    base
                    for base in record["bases"]
                    if not mangled_declaration.match(base.get("declarationId", ""))
                ]
        symbol_records[configuration] = records

    header_configuration = header_manifest.get("configurations", [{}])[0].get(
        "id", "win-x64-server"
    )
    header_shard = find_shard(header_manifest, header_configuration)
    if header_shard is None:
        raise SystemExit("header manifest has no shard")
    header_records = load_records(os.path.join(args.header, header_shard["path"]))

    header_declarations = {
        record["id"]: record
        for record in header_records
        if record.get("recordType") == "declaration"
    }
    header_declarations_by_compact_id = {
        re.sub(r",\s+", ",", declaration_id): declaration
        for declaration_id, declaration in header_declarations.items()
    }
    header_variants = {
        record["declarationId"]: record
        for record in header_records
        if record.get("recordType") == "variant"
        and record.get("configuration") == header_configuration
    }
    header_members = {}
    for record in header_records:
        if record.get("recordType") == "member":
            header_members.setdefault(record.get("declarationId"), []).append(record)

    symbol_declarations = {
        record["id"]: record
        for records in symbol_records.values()
        for record in records
        if record.get("recordType") == "declaration"
    }
    # Kept up to date as records are appended. Rebuilding it per class made the merge
    # quadratic, which at six thousand classes dominated the run.
    existing_keys = {
        (record.get("id"), configuration)
        for configuration, records in symbol_records.items()
        for record in records
    }
    existing_methods_by_declaration = {}
    for configuration, records in symbol_records.items():
        for record in records:
            if record.get("recordType") == "member":
                existing_methods_by_declaration.setdefault(
                    record.get("declarationId"), set()
                ).add(normalize_signature(record.get("id", "")))

    added_declarations = 0
    copied_fields = 0
    copied_methods = 0
    copied_layouts = 0
    copied_enum_values = 0
    processed_base_classes = set()

    # The generated headers declare only the members the generator recovered, so a class
    # it knew nothing about still parses -- as an empty class or as one carrying nothing
    # but its vtable and bases. `EatBlockGoal` is one of the latter: the header gives it
    # the 8-byte size of its base, while the real object holds a mob reference and timers.
    # Emitting that size would make the shim under-allocate every object it creates, so a
    # layout is used only when it is provably the real one:
    #
    #   - the class declares its own data members, and
    #   - those members' offsets match the ones the header generator annotated with, and
    #   - every base class it inherits from is trustworthy by the same rule.
    #
    # Types whose data lives entirely in a base class pass no test of their own; the few
    # families that are known to be complete are listed in layout-trust.txt.
    #
    # Fields inherit this judgement: an offset clang computed from an incomplete member
    # list reads and writes the wrong storage, so a class without a trusted layout also
    # gives up its field accessors.
    trusted_layouts = {}
    rejected_incomplete_layouts = 0
    rejected_mismatched_layouts = 0
    rejected_missing_sources = 0
    unverified_layouts = 0

    trusted_names = []
    if args.layout_trust and os.path.exists(args.layout_trust):
        with open(args.layout_trust, encoding="utf-8") as stream:
            trusted_names = [
                line.strip()
                for line in stream
                if line.strip() and not line.strip().startswith("#")
            ]

    def allow_listed(declaration_id):
        name = (
            declaration_id[len("bedrock:") :]
            if declaration_id.startswith("bedrock:")
            else declaration_id
        )
        for entry in trusted_names:
            if entry.endswith("*"):
                if name.startswith(entry[:-1]):
                    return True
            elif name == entry:
                return True
        return False

    def header_layout_trusted(declaration_id, stack=None):
        nonlocal \
            rejected_incomplete_layouts, \
            rejected_mismatched_layouts, \
            rejected_missing_sources
        nonlocal unverified_layouts
        if declaration_id in trusted_layouts:
            return trusted_layouts[declaration_id]
        stack = set() if stack is None else stack
        if declaration_id in stack:
            trusted_layouts[declaration_id] = False
            return False
        variant = header_variants.get(declaration_id)
        if variant is None:
            # The supplement covers the mc/ tree; a base from anywhere else (LeviLamina's
            # ll/ headers, the standard library) was laid out from its own complete
            # definition by the frontend.
            trusted_layouts[declaration_id] = True
            return True
        layout = variant.get("layout")
        if allow_listed(declaration_id):
            trusted_layouts[declaration_id] = True
            return True
        if not layout or not layout.get("fieldOffsets"):
            rejected_incomplete_layouts += 1
            trusted_layouts[declaration_id] = False
            return False
        annotated = source_member_offsets(
            header_declarations.get(declaration_id, {}).get("source"), args.header_root
        )
        if annotated is SOURCE_UNAVAILABLE:
            rejected_missing_sources += 1
            trusted_layouts[declaration_id] = False
            return False
        if annotated is None:
            unverified_layouts += 1
        elif annotated != [
            offset["bitOffset"] // 8 for offset in layout["fieldOffsets"]
        ]:
            rejected_mismatched_layouts += 1
            trusted_layouts[declaration_id] = False
            return False
        stack.add(declaration_id)
        trusted = all(
            header_layout_trusted(base.get("declarationId"), stack)
            for base in layout.get("bases", [])
        )
        stack.discard(declaration_id)
        trusted_layouts[declaration_id] = trusted
        return trusted

    # The carrier's deleting destructors hand the sized operator delete `sizeof(T)`, which
    # extract-class-sizes.py reads back per class. That size is the compiler's own, so it
    # is used as the layout size even where the header cannot prove one; the header's field
    # offsets are kept only when they describe the same, proven layout.
    class_sizes = {}
    if args.class_sizes and os.path.exists(args.class_sizes):
        with open(args.class_sizes, encoding="utf-8") as stream:
            class_sizes = json.load(stream)
    applied_class_sizes = 0
    counted_class_sizes = set()

    def class_size(declaration_id):
        name = (
            declaration_id[len("bedrock:") :]
            if declaration_id.startswith("bedrock:")
            else declaration_id
        )
        return class_sizes.get(name)

    def layout_for(declaration_id):
        """Layout to emit for a class, and whether its field offsets are usable."""
        nonlocal applied_class_sizes
        variant = header_variants.get(declaration_id)
        header_layout = variant.get("layout") if variant else None
        trusted = header_layout is not None and header_layout_trusted(declaration_id)
        header_size = header_layout.get("size", 0) if trusted else 0
        exact = class_size(declaration_id)
        if exact:
            if declaration_id not in counted_class_sizes:
                counted_class_sizes.add(declaration_id)
                applied_class_sizes += 1
            keep_fields = trusted and header_size == exact
            return (
                {
                    "size": exact,
                    "align": header_layout.get("align", 0) if keep_fields else 16,
                    "fieldOffsets": header_layout.get("fieldOffsets", [])
                    if keep_fields
                    else [],
                    "bases": [],
                },
                keep_fields,
            )
        if trusted and header_size:
            return (
                {
                    "size": header_size,
                    "align": header_layout.get("align", 0),
                    "fieldOffsets": header_layout.get("fieldOffsets", []),
                    "bases": [],
                },
                True,
            )
        return None, False

    def ensure_header_class(class_id):
        nonlocal added_declarations, copied_fields, copied_layouts
        if class_id in processed_base_classes:
            return
        processed_base_classes.add(class_id)

        bases = []
        for configuration, records in symbol_records.items():
            for record in records:
                if (
                    record.get("recordType") == "variant"
                    and record.get("declarationId") == class_id
                ):
                    bases.extend(record.get("bases", []))

        if class_id not in symbol_declarations and class_id in header_declarations:
            symbol_records[configurations[0]].append(
                copy.deepcopy(header_declarations[class_id])
            )
            symbol_declarations[class_id] = header_declarations[class_id]
            added_declarations += 1

            header_variant = header_variants.get(class_id, {})
            layout = header_variant.get("layout")
            header_class_bases = layout.get("bases", []) if layout else []
            clean_layout, keep_fields = layout_for(class_id)

            for configuration in configurations:
                if class_id not in variant_ids[configuration]:
                    variant = {
                        "recordType": "variant",
                        "declarationId": class_id,
                        "configuration": configuration,
                        "availability": "present",
                    }
                    if clean_layout is not None:
                        variant["layout"] = copy.deepcopy(clean_layout)
                        copied_layouts += 1
                    if header_class_bases:
                        variant["bases"] = copy.deepcopy(header_class_bases)
                        bases.extend(header_class_bases)
                    symbol_records[configuration].append(variant)
                    variant_ids[configuration].add(class_id)

                for header_member in header_members.get(class_id, []):
                    if header_member.get("kind") not in ("field", "static_field"):
                        continue
                    if not keep_fields:
                        continue
                    if header_member.get("id") in member_ids[configuration]:
                        continue
                    member = copy.deepcopy(header_member)
                    member["configuration"] = configuration
                    symbol_records[configuration].append(member)
                    member_ids[configuration].add(member.get("id"))
                    existing_keys.add((member.get("id"), configuration))
                    copied_fields += 1

        for base in bases:
            ensure_header_class(base.get("declarationId"))

    for class_name in classes:
        declaration_id = "bedrock:" + class_name
        symbol_declaration = symbol_declarations.get(declaration_id)
        if symbol_declaration is None:
            continue
        # A class the header tree does not describe still has a size when its deleting
        # destructor does, so the header facts are optional here.
        header_declaration = header_declarations.get(declaration_id, {})

        symbol_declaration["abstract"] = header_declaration.get(
            "abstract", symbol_declaration.get("abstract", False)
        )
        symbol_declaration["copyConstructible"] = header_declaration.get(
            "copyConstructible", symbol_declaration.get("copyConstructible", False)
        )
        symbol_declaration["triviallyCopyable"] = header_declaration.get(
            "triviallyCopyable", symbol_declaration.get("triviallyCopyable", False)
        )
        symbol_declaration["triviallyDestructible"] = header_declaration.get(
            "triviallyDestructible",
            symbol_declaration.get("triviallyDestructible", False),
        )

        header_variant = header_variants.get(declaration_id, {})
        layout = header_variant.get("layout")
        header_bases = layout.get("bases", []) if layout else []
        usable_layout, keep_fields = layout_for(declaration_id)

        # Index the variants once instead of rescanning every record for each class: at
        # three thousand classes the scan dominated the whole merge.
        for configuration in configurations:
            record = variant_by_id[configuration].get(declaration_id)
            if record is None:
                # The RTTI walk did not reach this class, but its size is known: keep a
                # variant so the layout still reaches the emitter.
                if usable_layout is None:
                    continue
                record = {
                    "recordType": "variant",
                    "declarationId": declaration_id,
                    "configuration": configuration,
                    "availability": "present",
                }
                symbol_records[configuration].append(record)
                variant_by_id[configuration][declaration_id] = record
                variant_ids[configuration].add(declaration_id)
            if usable_layout is not None:
                record["layout"] = copy.deepcopy(usable_layout)
                copied_layouts += 1
            if header_bases:
                existing = record.setdefault("bases", [])
                known = {base.get("declarationId") for base in existing}
                for base in header_bases:
                    if base.get("declarationId") not in known:
                        existing.append(copy.deepcopy(base))
                        known.add(base.get("declarationId"))

        existing_methods = existing_methods_by_declaration.get(declaration_id, ())
        for header_member in header_members.get(declaration_id, []):
            kind = header_member.get("kind")
            if kind in ("field", "static_field"):
                # A field offset derived from an incomplete member list points at the
                # wrong storage, so it is only copied with a trusted layout.
                if kind == "field" and not keep_fields:
                    continue
                for configuration in configurations:
                    member = copy.deepcopy(header_member)
                    member["configuration"] = configuration
                    key = (member.get("id"), configuration)
                    if key not in existing_keys:
                        symbol_records[configuration].append(member)
                        existing_keys.add(key)
                        copied_fields += 1
                continue

            if kind not in ("method", "constructor", "destructor", "operator"):
                continue
            identifier = header_member.get("id", "")
            if normalize_signature(identifier) in existing_methods:
                continue
            for configuration in configurations:
                member = copy.deepcopy(header_member)
                member["configuration"] = configuration
                member["inline"] = True
                member["defined"] = True
                key = (member.get("id"), configuration)
                if key not in existing_keys:
                    symbol_records[configuration].append(member)
                    existing_keys.add(key)
                    existing_methods_by_declaration.setdefault(
                        declaration_id, set()
                    ).add(normalize_signature(identifier))
                    copied_methods += 1

    base_class_ids = [
        base.get("declarationId")
        for records in symbol_records.values()
        for record in records
        if record.get("recordType") == "variant"
        for base in record.get("bases", [])
    ]
    for base_class_id in base_class_ids:
        if base_class_id:
            ensure_header_class(base_class_id)

    # Enum declarations carry the underlying integer type used by signatures. The symbol
    # stream has no type information, so copy every enum the header umbrella saw.
    for enum_id, enum_declaration in header_declarations.items():
        if enum_declaration.get("kind") != "enum":
            continue
        if enum_id not in symbol_declarations:
            symbol_records[configurations[0]].append(copy.deepcopy(enum_declaration))
            symbol_declarations[enum_id] = enum_declaration
            added_declarations += 1

        # Enumerators are platform independent and the coverage manifest needs a row in
        # every configuration, so each value record is copied into all of them.
        for header_member in header_members.get(enum_id, []):
            if header_member.get("kind") != "enum_value":
                continue
            for configuration in configurations:
                member = copy.deepcopy(header_member)
                member["configuration"] = configuration
                key = (member.get("id"), configuration)
                if key in existing_keys:
                    continue
                symbol_records[configuration].append(member)
                existing_keys.add(key)
                copied_enum_values += 1

    def base_type_name(text):
        name = (text or "").strip()
        while name.endswith("&") or name.endswith("*"):
            name = name[:-1].strip()
        name = (
            name.replace("const ", "")
            .replace(" const", "")
            .replace("volatile ", "")
            .replace(" volatile", "")
            .strip()
        )
        return name

    def split_template_arguments(text):
        arguments = []
        depth = 0
        start = 0
        for index, character in enumerate(text):
            if character == "<":
                depth += 1
            elif character == ">":
                depth -= 1
            elif character == "," and depth == 0:
                arguments.append(text[start:index].strip())
                start = index + 1
        arguments.append(text[start:].strip())
        return [argument for argument in arguments if argument]

    def referenced_record_names(text):
        name = base_type_name(text)
        if not name:
            return []
        prefix = None
        for candidate in (
            "std::vector<",
            "std::optional<",
            "std::unordered_set<",
            "std::shared_ptr<",
            "std::unique_ptr<",
        ):
            if name.startswith(candidate) and name.endswith(">"):
                prefix = candidate
                break
        if prefix is not None:
            arguments = split_template_arguments(name[len(prefix) : -1])
            return referenced_record_names(arguments[0]) if arguments else []
        if name.startswith("std::pair<") and name.endswith(">"):
            names = []
            for argument in split_template_arguments(name[len("std::pair<") : -1]):
                names.extend(referenced_record_names(argument))
            return names
        if name.startswith("std::") or "(" in name or ")" in name:
            return []
        return [name]

    # Record types that appear by value in signatures need a declaration with a layout
    # and the trivially-copyable fact. Copy those header declarations as dependencies.
    # Sequence element types are referenced through std::vector/std::optional, so recurse
    # into the template arguments instead of looking only at the outermost spelling.
    referenced_types = set()
    for records in symbol_records.values():
        for record in records:
            if record.get("recordType") != "member":
                continue
            signature = record.get("signature") or {}
            referenced_types.update(
                referenced_record_names(signature.get("returnType", ""))
            )
            for parameter in signature.get("parameters", []):
                referenced_types.update(
                    referenced_record_names(parameter.get("type", ""))
                )

    for type_name in sorted(referenced_types):
        if (
            not type_name
            or type_name.startswith("std::")
            or "(" in type_name
            or ")" in type_name
        ):
            continue
        declaration_id = "bedrock:" + type_name
        header_declaration = header_declarations.get(
            declaration_id
        ) or header_declarations_by_compact_id.get(declaration_id)
        if header_declaration is None or header_declaration.get("kind") not in (
            "class",
            "struct",
            "union",
        ):
            continue
        header_variant = header_variants.get(
            header_declaration.get("id", declaration_id), {}
        )
        layout = header_variant.get("layout")
        # The declaration is copied either way so the type stays in the surface with its
        # symbol and vtable members. Only a proven size is attached: the shim sizes its
        # allocations and by-value copies from it.
        usable_layout, _ = layout_for(header_declaration.get("id", declaration_id))

        if declaration_id in symbol_declarations:
            # The RTTI walk already emitted a bare declaration for this base class. Keep it
            # but take the header's abstract fact so ownership paths treat it correctly.
            symbol_declarations[declaration_id]["abstract"] = header_declaration.get(
                "abstract", symbol_declarations[declaration_id].get("abstract", False)
            )
        else:
            copied_declaration = copy.deepcopy(header_declaration)
            # The demangler omits spaces after template commas; make the dependency id match
            # the spelling used by the symbol signatures.
            compact_name = re.sub(
                r",\s+", ",", copied_declaration.get("qualifiedName", "")
            )
            if compact_name != copied_declaration.get("qualifiedName"):
                copied_declaration["qualifiedName"] = compact_name
                copied_declaration["id"] = "bedrock:" + compact_name
                declaration_id = copied_declaration["id"]
            symbol_records[configurations[0]].append(copied_declaration)
            symbol_declarations[declaration_id] = copied_declaration
            added_declarations += 1

        clean_layout = None
        if usable_layout is not None:
            clean_layout = copy.deepcopy(usable_layout)
            clean_layout["fieldOffsets"] = []
            header_bases = layout.get("bases", []) if layout else []
            if header_bases:
                clean_layout["bases"] = copy.deepcopy(header_bases)
        for configuration in configurations:
            if declaration_id in variant_ids[configuration]:
                continue
            variant = {
                "recordType": "variant",
                "declarationId": declaration_id,
                "configuration": configuration,
                "availability": "present",
            }
            if clean_layout is not None:
                variant["layout"] = copy.deepcopy(clean_layout)
                copied_layouts += 1
            symbol_records[configuration].append(variant)
            variant_ids[configuration].add(declaration_id)

    # The validator keys declarations and members by id and variants by declarationId; two
    # symbols that demangle to the same member (an inner scope collapsing into its outer
    # class, for instance) would otherwise produce the same key twice and fail the emit.
    dropped = 0
    for configuration in configurations:
        seen = set()
        unique = []
        for record in symbol_records[configuration]:
            key = (
                record.get("declarationId")
                if record.get("recordType") == "variant"
                else record.get("id"),
                configuration,
                record.get("recordType"),
            )
            if key[0] is None:
                unique.append(record)
                continue
            if key in seen:
                dropped += 1
                continue
            seen.add(key)
            unique.append(record)
        symbol_records[configuration] = unique

    os.makedirs(args.output, exist_ok=True)
    shard_paths = {}
    for configuration in configurations:
        text = serialize(symbol_records[configuration])
        path = f"{configuration}.jsonl"
        with open(
            os.path.join(args.output, path), "w", encoding="utf-8", newline="\n"
        ) as stream:
            stream.write(text)
        shard_paths[configuration] = (path, text)

    declarations = 0
    variants = 0
    members = 0
    for records in symbol_records.values():
        declarations += sum(
            1 for record in records if record.get("recordType") == "declaration"
        )
        variants += sum(
            1 for record in records if record.get("recordType") == "variant"
        )
        members += sum(1 for record in records if record.get("recordType") == "member")
    # Declarations are only emitted in the first configuration shard.
    declarations = sum(
        1
        for record in symbol_records[configurations[0]]
        if record.get("recordType") == "declaration"
    )

    for shard in symbol_manifest.get("shards", []):
        configuration = shard.get("configuration")
        if configuration in shard_paths:
            path, text = shard_paths[configuration]
            shard["path"] = path
            shard["records"] = sum(1 for record in symbol_records[configuration])
            shard["sha256"] = sha256_text(text)
            shard["sha256Compressed"] = sha256_text(text)

    symbol_manifest["counts"] = {
        "declarations": declarations,
        "variants": variants,
        "members": members,
    }

    # The merged IR carries header facts the symbol stream does not have (enum values,
    # layouts), so its schema version is at least the header manifest's.
    def version_key(text):
        return tuple(int(part) for part in (text or "0").split(".") if part.isdigit())

    if version_key(header_manifest.get("irVersion")) > version_key(
        symbol_manifest.get("irVersion")
    ):
        symbol_manifest["irVersion"] = header_manifest.get("irVersion")
    with open(
        os.path.join(args.output, "ir-manifest.json"),
        "w",
        encoding="utf-8",
        newline="\n",
    ) as stream:
        json.dump(
            symbol_manifest,
            stream,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=False,
        )
        stream.write("\n")

    print(
        f"merged: layouts={copied_layouts} fields={copied_fields} methods={copied_methods} "
        f"enum values={copied_enum_values} declarations+={added_declarations} duplicates dropped={dropped}"
    )
    print(
        f"layouts rejected: the header declares no data members={rejected_incomplete_layouts} "
        f"annotated offsets disagree={rejected_mismatched_layouts} "
        f"header source unavailable={rejected_missing_sources} "
        f"no annotated header to check={unverified_layouts}"
    )
    print(f"class sizes applied: {applied_class_sizes}")


if __name__ == "__main__":
    main()
