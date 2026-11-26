"""Generate explicit instantiations for by-value template return types.

A signature can name a class template specialization (`Bedrock::Result<T>`,
`GameplayHandlerResult<CoordinatorResult>`) while the specialization itself never enters
the AST: a return type in a declaration does not require the type to be complete. The
prelude is appended to the umbrella so the second frontend pass instantiates every
specialization that a return type names.

Signatures print `Bedrock::Result<T>` while the template's second argument is defaulted;
for that template a missing second argument is filled with `std::error_code`.

Only arguments that are complete at the prelude are instantiated: a scalar, an enum
(always complete in the emitted IR), `std::error_code`, `std::basic_string<char>`, a
pointer (the pointee stays opaque), or a class with a recorded layout. An incomplete
argument would make the translation unit stop at clang's error limit.
"""

import json
import pathlib
import sys

BUILTIN = {
    "void",
    "bool",
    "char",
    "signed char",
    "unsigned char",
    "short",
    "unsigned short",
    "int",
    "unsigned int",
    "long",
    "unsigned long",
    "long long",
    "unsigned long long",
    "float",
    "double",
    "long double",
    "wchar_t",
    "char8_t",
    "char16_t",
    "char32_t",
}
VALUE_TYPES = {"std::error_code", "std::basic_string<char>", "std::string"}

# Templates whose specialization declaration the ABI planner can carry as a by-value
# result. Both definitions are complete in the umbrella; instantiating one here keeps
# its declaration at the original header, inside the source root.
TEMPLATES = {
    "Bedrock::Result",
    "GameplayHandlerResult",
}


def split_top_level(text):
    args = []
    depth = 0
    start = 0
    for index, char in enumerate(text):
        if char == "<":
            depth += 1
        elif char == ">":
            depth -= 1
        elif char == "," and depth == 0:
            args.append(text[start:index].strip())
            start = index + 1
    args.append(text[start:].strip())
    return args


def split_template(text):
    if not text.endswith(">"):
        return None, None
    opening = text.find("<")
    if opening <= 0:
        return None, None
    name = text[:opening].strip()
    if "<" in name or not name:
        return None, None
    return name, text[opening + 1 : -1]


def load_complete_names(ir):
    names = set()
    layouts = set()
    enums = set()
    records = {}
    for shard in sorted(ir.glob("*.jsonl")):
        with shard.open(encoding="utf-8") as stream:
            for line in stream:
                record = json.loads(line)
                if record.get("recordType") == "variant":
                    layouts.add(record.get("declarationId"))
                elif record.get("recordType") == "declaration":
                    records[record.get("id")] = record.get("qualifiedName")
                    if record.get("kind") == "enum":
                        enums.add(record.get("qualifiedName"))
    names.update(
        name for declaration, name in records.items() if declaration in layouts
    )
    names.update(enums)
    return names


def main():
    ir = pathlib.Path(sys.argv[1])
    out = pathlib.Path(sys.argv[2])
    complete_names = load_complete_names(ir)

    def complete_argument(argument):
        if argument in BUILTIN or argument in VALUE_TYPES:
            return True
        if argument.endswith("*"):
            return True
        if "<" in argument:
            return False
        return argument in complete_names

    instantiations = set()
    for shard in sorted(ir.glob("*.jsonl")):
        with shard.open(encoding="utf-8") as stream:
            for line in stream:
                record = json.loads(line)
                signature = record.get("signature")
                if not signature:
                    continue
                returned = (signature.get("returnType") or "").strip()
                name, inner = split_template(returned)
                if name is None or name not in TEMPLATES:
                    continue
                arguments = split_top_level(inner)
                if name == "Bedrock::Result" and len(arguments) == 1:
                    arguments.append("std::error_code")
                if len(arguments) != (1 if name == "GameplayHandlerResult" else 2):
                    continue
                if any(
                    argument == ""
                    or "..." in argument
                    or "<lambda" in argument
                    or "decltype" in argument
                    or argument.startswith("auto")
                    for argument in arguments
                ):
                    continue
                if not all(complete_argument(argument) for argument in arguments):
                    continue
                instantiations.add((name, tuple(arguments)))

    # A sizeof probe forces an implicit instantiation whose declaration keeps the
    # template header's location; an explicit instantiation definition would move the
    # specialization to this prelude, outside the source root, and the IR would drop it.
    lines = [
        "// <auto-generated/> by-value template specializations used by return types",
        "",
    ]
    for name, arguments in sorted(instantiations):
        spelling = ", ".join(arguments)
        lines.append(
            f"static_assert(sizeof(::{name}<{spelling}>) > 0, "
            '"instantiate the by-value result specialization");'
        )
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(instantiations)} template instantiations -> {out}")


if __name__ == "__main__":
    main()
