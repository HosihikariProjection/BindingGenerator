#!/usr/bin/env python3
"""Recover ``sizeof`` for every class that has a scalar deleting destructor.

The Windows carrier declares the data members of only part of its classes, so the header
supplement cannot prove a layout for the rest. The compiler does know each size, though:
a class with a virtual destructor gets a ``??_G`` thunk that calls the sized
``operator delete`` with ``sizeof(T)``. Reading that immediate per thunk recovers the size
exactly, without a type stream.

Usage:
  python extract-class-sizes.py --pdb <bedrock_server.pdb> --exe <bedrock_server.exe> \\
      --output artifacts/class-sizes.json
"""

import argparse
import json
import os
import re
import struct
import subprocess
import sys

import capstone


# Address of the sized operator delete in bedrock_server.exe 1.21.3.01. The deleting
# destructors all tail into it; another carrier build needs its own --delete-target.
SIZED_DELETE_CALL = "0x1423d7270"


def run(command):
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"{command[0]} failed: {result.stderr.strip()}")
    return result.stdout


def read_publics(pdb, llvm_root, cache):
    """Return the public symbol dump, reusing a cached copy next to the output."""
    if cache and os.path.exists(cache):
        with open(cache, encoding="utf-8", errors="replace") as stream:
            return stream.read()
    pdbutil = os.path.join(llvm_root, "bin", "llvm-pdbutil.exe")
    if not os.path.exists(pdbutil):
        pdbutil = os.path.join(llvm_root, "bin", "llvm-pdbutil")
    text = run([pdbutil, "dump", "-publics", pdb])
    if cache:
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        with open(cache, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(text)
    return text


def deleting_destructors(publics):
    name_pattern = re.compile(r"`(\?\?_G[^`]*)`")
    addr_pattern = re.compile(r"addr = ([0-9A-Fa-f]{4}):(\d+)")
    pending = None
    for line in publics.splitlines():
        match = name_pattern.search(line)
        if match:
            pending = match.group(1)
            continue
        if pending:
            address = addr_pattern.search(line)
            if address:
                yield pending, int(address.group(1), 16), int(address.group(2))
                pending = None


def text_section(executable):
    with open(executable, "rb") as stream:
        data = stream.read()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise SystemExit("not a PE image")
    sections = struct.unpack_from("<H", data, pe + 6)[0]
    optional = struct.unpack_from("<H", data, pe + 20)[0]
    table = {}
    for index in range(sections):
        offset = pe + 24 + optional + index * 40
        name = data[offset:offset + 8].rstrip(b"\0").decode(errors="replace")
        vsize, va, rawsize, rawptr = struct.unpack_from("<IIII", data, offset + 8)
        table[index + 1] = (va, rawptr, vsize)
    return data, table


def destructor_sizes(executable, entries, delete_call=SIZED_DELETE_CALL):
    """Byte immediate the sized delete receives inside each deleting destructor."""
    data, sections = text_section(executable)
    disassembler = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    disassembler.skipdata = True
    sizes = {}
    for name, section, offset in entries:
        va, raw, vsize = sections.get(section, (None, None, None))
        if va is None or offset >= vsize:
            continue
        start = raw + offset
        address = 0x140000000 + offset + va
        # A folded thunk is a lone jump: follow it once before reading the body.
        body = data[start:start + 800]
        instructions = list(disassembler.disasm_lite(body, address))
        if instructions and instructions[0][2] == "jmp" and instructions[0][3].startswith("0x"):
            target = int(instructions[0][3], 16) - 0x140000000
            for va2, raw2, vsize2 in sections.values():
                if va2 <= target < va2 + vsize2:
                    start = raw2 + target - va2
                    instructions = list(disassembler.disasm_lite(data[start:start + 800], target + 0x140000000))
                    break
        last = None
        size = None
        for _, _, mnemonic, operands in instructions:
            if mnemonic == "mov":
                destination, _, source = operands.partition(", ")
                if destination.strip() in ("edx", "rdx") and source.strip().startswith("0x"):
                    last = int(source.strip(), 16)
            elif mnemonic == "call":
                if operands.strip() == delete_call and last:
                    size = last
                last = None
            elif mnemonic in ("jmp", "ret", "int3"):
                last = None
            if mnemonic in ("ret", "int3"):
                break
        if size:
            sizes[name] = size
    return sizes


def demangle(names, llvm_root):
    undname = os.path.join(llvm_root, "bin", "llvm-undname.exe")
    if not os.path.exists(undname):
        undname = os.path.join(llvm_root, "bin", "llvm-undname")
    classes = {}
    chunk = []
    length = 0
    batches = []
    for name in names:
        if length + len(name) > 6000 and chunk:
            batches.append(chunk)
            chunk, length = [], 0
        chunk.append(name)
        length += len(name) + 1
    if chunk:
        batches.append(chunk)
    for batch in batches:
        current = None
        for line in run([undname] + batch).splitlines():
            if line in names:
                current = line
                continue
            match = re.search(r"__cdecl (.*)::`scalar deleting d", line)
            if current and match:
                classes[current] = match.group(1).strip()
    return classes


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pdb", required=True)
    parser.add_argument("--exe", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument(
        "--llvm-root",
        default=os.environ.get(
            "HOSIHIKARI_LLVM", os.path.join(os.environ.get("LOCALAPPDATA", ""), "Temp", "hosihikari-llvm",
                                            "clang+llvm-23.1.0-x86_64-pc-windows-msvc")),
    )
    parser.add_argument("--publics-cache", default="")
    parser.add_argument("--delete-target", default=SIZED_DELETE_CALL,
                        help="Address of the sized operator delete in this carrier build.")
    args = parser.parse_args()

    publics = read_publics(args.pdb, args.llvm_root, args.publics_cache)
    entries = list(deleting_destructors(publics))
    sizes = destructor_sizes(args.exe, entries, args.delete_target)
    if not sizes:
        raise SystemExit(f"no deleting destructor calls {args.delete_target}; pass --delete-target")
    classes = demangle(list(sizes), args.llvm_root)

    by_class = {}
    for mangled, size in sizes.items():
        name = classes.get(mangled)
        if name:
            by_class.setdefault(name, set()).add(size)
    # Two different sizes for one name means the mapping caught something else.
    conflicts = {name: sorted(values) for name, values in by_class.items() if len(values) > 1}
    resolved = {name: values.pop() for name, values in by_class.items() if len(values) == 1}

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(resolved, stream, indent=0, sort_keys=True)
        stream.write("\n")
    print(
        f"class sizes: {len(entries)} deleting destructors, {len(sizes)} sized, "
        f"{len(resolved)} classes, {len(conflicts)} conflicting names"
    )
    if conflicts:
        for name in list(conflicts)[:5]:
            print(f"  conflict {name}: {conflicts[name]}", file=sys.stderr)


if __name__ == "__main__":
    main()
