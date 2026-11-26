#!/usr/bin/env python3
"""Classify a LeviLamina ``mc/`` header tree by its API availability markers.

The generated ``mc/`` headers mark every declaration that the runtime exports
with ``MCAPI`` (``MCFOLD`` for identical-COMDAT-folded symbols, ``MCTAPI`` for
explicit template specialisations). ``MCNAPI`` is ``[[deprecated]] MCAPI``: the
declaration is known but the runtime does not expose it. That makes the
non-``MCNAPI`` marker set the usable capability surface of the shipped server,
which is the whitelist the binding generator can target.

The parser is line based on purpose. The headers are machine generated, so each
marker sits at the start of a declaration line and each type opens with its
brace on the declaration line or on the last line of a multi-line base clause.

Outputs two TSV files in ``--output``:

``mcapi-whitelist.tsv``
    one row per marker: status, marker, scope, file, line, declaration, and
    whether the row sits inside a ``LL_PLAT_C`` client-only guard.

``mcapi-scopes.tsv``
    one row per declaring scope with its usable and deprecated marker counts,
    sorted by usable count.
"""

import argparse
import collections
import os

MARKERS = {
    "MCAPI": ("usable", "api"),
    "MCFOLD": ("usable", "fold"),
    "MCTAPI": ("usable", "tapi"),
    "MCNAPI": ("deprecated", "napi"),
}


def scope_of(frames):
    parts = [name for kind, name, _ in frames if kind == "namespace"]
    for kind, name, _ in reversed(frames):
        if kind != "namespace":
            parts.append(name)
            break
    return "::".join(parts)


def parse_file(path, root, rows):
    """Yield marker rows for one header.

    ``frames`` holds the open scopes as ``(kind, name, depth_at_entry)``. A type
    whose base clause wraps onto following lines stays ``pending`` until its
    brace arrives.
    """
    import re

    re_namespace = re.compile(r"^\s*namespace\s+([A-Za-z_][\w:]*)\s*\{")
    re_type = re.compile(
        r"^\s*(?:template\s*<.*>\s*)?"
        r"(class|struct|enum\s+class|enum\s+struct|enum|union)\s+(\w+)\b"
    )
    # A few declarations put an inline specifier before the marker.
    re_marker = re.compile(
        r"^\s*(?:static\s+|virtual\s+|inline\s+)*(%s)\b" % "|".join(MARKERS)
    )

    frames = []
    pending = None
    depth = 0
    client_guards = []
    relative = os.path.relpath(path, root).replace("\\", "/")

    with open(path, encoding="utf-8", errors="replace") as stream:
        for line_number, line in enumerate(stream, 1):
            stripped = line.strip()
            if stripped.startswith("#if"):
                client_guards.append("LL_PLAT_C" in stripped)
            elif stripped.startswith("#endif"):
                if client_guards:
                    client_guards.pop()
            elif stripped.startswith("#else"):
                if client_guards:
                    client_guards[-1] = not client_guards[-1]

            body = line.split("//", 1)[0]
            if body.strip():
                marker = re_marker.match(line)
                if marker:
                    status, _ = MARKERS[marker.group(1)]
                    rows.append(
                        (
                            status,
                            marker.group(1),
                            scope_of(frames),
                            relative,
                            line_number,
                            stripped,
                            bool(any(client_guards)),
                        )
                    )

                namespace = re_namespace.match(body)
                if namespace:
                    pending = ("namespace", namespace.group(1))
                else:
                    type_open = re_type.match(body)
                    if type_open and ";" not in body:
                        pending = ("type", type_open.group(2))

            for character in body:
                if character == "{":
                    if pending is not None:
                        frames.append((pending[0], pending[1], depth))
                        pending = None
                    depth += 1
                elif character == "}":
                    depth -= 1
                    while frames and depth <= frames[-1][2]:
                        frames.pop()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--root",
        required=True,
        help="LeviLamina checkout containing the generated mc/ headers",
    )
    parser.add_argument("--output", required=True)
    parser.add_argument(
        "--sources",
        default="src/mc,src-server/mc",
        help="comma separated header roots, server overlay last",
    )
    args = parser.parse_args()

    roots = [entry.strip() for entry in args.sources.split(",") if entry.strip()]
    rows = []
    for relative_root in roots:
        for directory, _, files in os.walk(os.path.join(args.root, relative_root)):
            for name in sorted(files):
                if name.endswith(".h"):
                    parse_file(os.path.join(directory, name), args.root, rows)

    usable = collections.Counter()
    deprecated = collections.Counter()
    scope_file = {}
    totals = collections.Counter()
    files_with_usable = set()

    for status, marker, scope, relative, _, _, _ in rows:
        totals[(status, marker)] += 1
        if status == "usable":
            usable[scope] += 1
            files_with_usable.add(relative)
        else:
            deprecated[scope] += 1
        scope_file.setdefault(scope, relative)

    os.makedirs(args.output, exist_ok=True)
    with open(
        os.path.join(args.output, "mcapi-whitelist.tsv"),
        "w",
        encoding="utf-8",
        newline="",
    ) as stream:
        stream.write("status\tmarker\tscope\tfile\tline\tdecl\tclient_only\n")
        for row in rows:
            stream.write("\t".join(str(value) for value in row) + "\n")

    with open(
        os.path.join(args.output, "mcapi-scopes.tsv"), "w", encoding="utf-8", newline=""
    ) as stream:
        stream.write("scope\tusable\tdeprecated\tshare\tfile\n")
        for scope in sorted(usable, key=lambda key: (-usable[key], key)):
            usable_count = usable[scope]
            deprecated_count = deprecated.get(scope, 0)
            share = usable_count / (usable_count + deprecated_count)
            stream.write(
                f"{scope}\t{usable_count}\t{deprecated_count}\t"
                f"{share:.4f}\t{scope_file.get(scope, '')}\n"
            )

    total_usable = sum(
        value for (status, _), value in totals.items() if status == "usable"
    )
    total_deprecated = sum(
        value for (status, _), value in totals.items() if status == "deprecated"
    )
    print(
        f"usable {total_usable} / deprecated {total_deprecated} / total {total_usable + total_deprecated}"
    )
    for (status, marker), value in sorted(totals.items()):
        print(f"  {status:10s} {marker:7s} {value}")
    print(f"scopes with usable markers {len(usable)}; files {len(files_with_usable)}")


if __name__ == "__main__":
    main()
