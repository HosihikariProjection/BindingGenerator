#!/usr/bin/env python3
"""Rank header-declared types that have symbols but are not bound yet.

Symbol discovery reports RTTI spellings, not declarations: a type shows up as
``VBlockDescriptor`` (kind letter glued to the name) or ``ScriptActor::ScriptModuleMinecraft``
(namespace reversed after the type). A type is bindable only when both facts
exist — a symbol from the debug database and a declaration in the generated
headers — so this script resolves each discovered spelling against the header
tree and reports what the selected class list still leaves out.

The header side is every class/struct/union/enum declaration under the header
roots. The symbol side is the discovery TSV (``class<TAB>symbols``). Resolution
tries the name as written, its reversed namespace form, and the same two with a
leading RTTI kind prefix removed; a leaf-name match is the last resort.

Library namespaces are dropped: their types are std/entt/webrtc and friends,
which are neither part of the game surface nor ours to bind.

Symbol counts are summed per resolved type, so several RTTI spellings for one
type collapse into one row. The number is a ranking aid, not a coverage figure:
discovery counts overlap between spellings.
"""

import argparse
import collections
import os
import re

LIBRARY_NAMESPACES = {
    "std",
    "entt",
    "webrtc",
    "rtc",
    "cricket",
    "leveldb",
    "fmt",
    "rapidjson",
    "nlohmann",
    "glm",
    "gsl",
    "pfr",
    "ctre",
    "concurrentqueue",
    "moodycamel",
    "nonstd",
    "asio",
    "re2",
    "snappy",
    "zlib",
    "openssl",
}

re_namespace = re.compile(r"^\s*namespace\s+([A-Za-z_][\w:]*)\s*\{")
re_type = re.compile(
    r"^\s*(?:template\s*<.*>\s*)?"
    r"(class|struct|enum\s+class|enum\s+struct|enum|union)\s+(\w+)\b"
)
re_kind_prefix = re.compile(r"^(?:PE)?(?:AEB)?(?:SH)?([VU])(?=[A-Z])")


def header_types(root, sources):
    """Return {qualified name: relative file} for every declared type.

    The file column is relative to the parent of the first source root, which is the form
    the layout header lists use (`mc/world/actor/Actor.h`, not `src/mc/world/actor/Actor.h`).
    """
    types = {}
    base = os.path.join(root, os.path.dirname(sources[0])) if sources else root
    for source in sources:
        for directory, _, files in os.walk(os.path.join(root, source)):
            for name in sorted(files):
                if not name.endswith(".h"):
                    continue
                path = os.path.join(directory, name)
                relative = os.path.relpath(path, base).replace("\\", "/")
                frames = []
                pending = None
                depth = 0
                with open(path, encoding="utf-8", errors="replace") as stream:
                    for line in stream:
                        body = line.split("//", 1)[0]
                        if body.strip():
                            namespace = re_namespace.match(body)
                            if namespace:
                                pending = ("namespace", namespace.group(1))
                            else:
                                declared = re_type.match(body)
                                if declared and ";" not in body:
                                    pending = ("type", declared.group(2))
                        for character in body:
                            if character == "{":
                                if pending is not None:
                                    frames.append((pending[0], pending[1], depth))
                                    if pending[0] == "type":
                                        qualified = [
                                            n for k, n, _ in frames if k == "namespace"
                                        ]
                                        qualified.append(pending[1])
                                        types.setdefault("::".join(qualified), relative)
                                    pending = None
                                depth += 1
                            elif character == "}":
                                depth -= 1
                                while frames and depth <= frames[-1][2]:
                                    frames.pop()
    return types


def is_library(name):
    return any(segment in LIBRARY_NAMESPACES for segment in name.split("::"))


def spellings(name):
    """Yield the plausible header spellings of a discovered RTTI name."""
    forms = [name, "::".join(reversed(name.split("::")))]
    prefix = re_kind_prefix.match(name)
    if prefix:
        stripped = name[prefix.end() :]
        forms += [stripped, "::".join(reversed(stripped.split("::")))]
    return forms


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, help="header checkout root")
    parser.add_argument("--sources", default="src/mc,src-server/mc")
    parser.add_argument(
        "--classes", required=True, help="discovery TSV: class<TAB>symbols"
    )
    parser.add_argument(
        "--selected", help="class list already bound, one name per line"
    )
    parser.add_argument("--output", required=True)
    parser.add_argument("--min-symbols", type=int, default=1)
    args = parser.parse_args()

    sources = [entry for entry in args.sources.split(",") if entry]
    types = header_types(args.root, sources)
    leaves = {}
    for name in types:
        leaves.setdefault(name.split("::")[-1], name)

    discovered = []
    with open(args.classes, encoding="utf-8") as stream:
        next(stream)
        for line in stream:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2 and parts[1].isdigit():
                discovered.append((parts[0], int(parts[1])))

    matched = collections.Counter()
    for name, symbols in discovered:
        if is_library(name):
            continue
        resolved = None
        forms = spellings(name)
        for form in forms:
            if form in types:
                resolved = form
                break
        if resolved is None:
            for form in forms:
                candidate = leaves.get(form.split("::")[-1])
                if candidate and not is_library(candidate):
                    resolved = candidate
                    break
        if resolved is not None:
            matched[resolved] += symbols

    selected = set()
    if args.selected:
        with open(args.selected, encoding="utf-8") as stream:
            for line in stream:
                line = line.strip()
                if line and not line.startswith("#"):
                    selected.add(line.split("\t")[0])
    selected_leaves = {name.split("::")[-1] for name in selected}

    rows = [
        (name, symbols, types[name])
        for name, symbols in matched.items()
        if symbols >= args.min_symbols
        and name not in selected
        and name.split("::")[-1] not in selected_leaves
    ]
    rows.sort(key=lambda row: (-row[1], row[0]))

    with open(args.output, "w", encoding="utf-8", newline="") as stream:
        stream.write("type\tsymbols\tfile\n")
        for name, symbols, path in rows:
            stream.write(f"{name}\t{symbols}\t{path}\n")

    print(
        f"header types {len(types)}; matched {len(matched)}; "
        f"selected {len(selected)}; candidates {len(rows)}"
    )
    for name, symbols, _ in rows[:15]:
        print(f"  {symbols:5d}  {name}")


if __name__ == "__main__":
    main()
