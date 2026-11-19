# Symbol IR frontend (Bedrock 1.21.3)

Extracts a symbol-level IR from a publics-only PDB and an ELF symbol table. It exists to establish the third API surface without header assumptions. `--output` writes the prototype JSON; `--ir-output` writes formal IR 1.4.0 shards and a manifest.

## Inputs

- `--pdb`: the 1.21.3 `bedrock_server.pdb` (publics/globals, vtable and RTTI symbols; the TPI type stream is empty).
- `--exe`: the matching `bedrock_server.exe`, used for section RVAs and vtable bytes.
- `--elf`: optional `bedrock_server_symbols.debug`, used for the game `.symtab` and `.rela.dyn` vtable relocations. When omitted, the frontend emits only the `win-x64-server` configuration, which is what the synthetic fixture uses.
- `--classes`: comma-separated class names, currently `Actor,BlockPos`.

## Run

```powershell
./frontend/run-symbols.ps1 `
  -Pdb ../../BDS/bin/bedrock_server.pdb `
  -Exe ../../BDS/bin/bedrock_server.exe `
  -Elf ../../BDS/bin/bedrock_server_symbols.debug `
  -Output artifacts/symbol-ir-1.21.3/prototype.json `
  -Classes Actor,BlockPos
```

The script builds `hosihikari-symbol-ir-frontend` and writes a single prototype JSON document. Add `-IrOutput <dir>` to write `hosihikari.bindings.ir/1` 1.4.0 shards plus `ir-manifest.json`:

```powershell
./frontend/run-symbols.ps1 -Pdb <pdb> -Exe <pe> -Elf <debug> -IrOutput artifacts/symbol-ir/ir -NoPrototype
dotnet run --project ../src/Hosihikari.Binding.Generator/Hosihikari.Binding.Generator.csproj -c Release -- validate-ir artifacts/symbol-ir/ir/ir-manifest.json artifacts/symbol-ir/ir
```

## What the symbols provide

- Mangled public symbols and addresses from both images.
- Demangled method names, parameter types, `const`, `static`, and MSVC access specifiers. Member ids include the `const` qualifier, so const and non-const overloads remain distinct records.
- Windows return types (MSVC mangling) merged into Linux records by `qualified name + parameters + const`.
- Vtable membership: Windows slots are read from the PE as image-base VAs; Linux slots are reconstructed from `R_X86_64_RELATIVE` addends in `.rela.dyn`.
- No field layout, class size, field types, or `inline`/`defined` facts. The ELF DWARF covers only NetherNet/webrtc, and the PDB type stream is stripped.

## Current slice

| Class | Windows methods | Linux methods | Merged | Windows vtable slots | Linux vtable slots |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Actor` | 635 | 823 | 634 | 153 | 151 |
| `BlockPos` | 14 | 15 | 12 | 0 | 0 |

Linux includes local/inline functions that the Windows publics omit, which explains most Linux-only entries. The two vtable sizes differ by one slot; stores the per-configuration index rather than assuming a shared layout.

## Output shape

```json
{
  "prototype": "hosihikari.bedrock.symbols/1",
  "version": "1.21.3",
  "classes": [
    {
      "name": "Actor",
      "vtables": { "win-x64-server": { "symbol": "??_7Actor@@6B@", "address": "0x2da38f0" } },
      "methods": [
        {
          "key": "Actor::canAttack(Actor*,bool)const",
          "returnType": "bool",
          "parameters": ["Actor*", "bool"],
          "virtual": true,
          "win-x64-server": { "symbol": "?...", "rva": "0x...", "vtableIndex": 12 },
          "linux-x64-server": { "symbol": "_ZNK5Actor9canAttack...", "address": "0x...", "vtableIndex": 12 }
        }
      ]
    }
  ]
}
```

## RTTI inheritance

The ELF reader resolves `_ZTI` objects through `.rela.dyn` addends and reads `__class_type_info`, `__si_class_type_info`, and `__vmi_class_type_info` structures. For the 1.21.3 build it yields:

- `Actor : ActorStatusProvider`
- `Mob : Actor`
- `Player : Mob`

Results are written to `variant.bases` (IR 1.4.1+), and the header supplement adds `declaration.triviallyCopyable` in IR 1.5.0 and `declaration.triviallyDestructible` in IR 1.5.1. The PDB publics for this build contain no `??_R*` symbols for game classes, so Windows inheritance comes from the header supplement or stays empty.

## Header supplement

`frontend/run-levilamina-headers.ps1` generates a header supplement from a LeviLamina v0.13.5 checkout (`bdslibrary 1.21.3.01`). The frontend flattens anonymous unions/structs into named fields, so `ll::math::intN3<BlockPos>` contributes `x`/`y`/`z`. `frontend/merge-symbol-ir.py` then merges the supplement into the symbol IR:

```powershell
python frontend/merge-symbol-ir.py `
  --symbol artifacts/symbol-ir-1.21.3/ir `
  --header artifacts/llbds-header-ir `
  --output artifacts/symbol-ir-1.21.3/merged `
  --classes Actor,BlockPos
```

The merge:

- copies class traits (`abstract`, `copyConstructible`, `triviallyCopyable`, `triviallyDestructible`) and instance/static field members into both configurations;
- copies a merged class layout only when it has fields or bases, and drops header vtable slots (the symbol IR keeps per-configuration `vtableIndex`);
- copies every enum declaration and every record declaration referenced by a signature with a by-value type, recursing into `std::vector`, `std::optional`, `std::pair`, and smart-pointer template arguments so sequence element records get a declaration and a size/align-only layout (template records use the demangler's compact `A<B,int>` spelling);
- moves `layout.bases` into `variant.bases`, deduplicating against RTTI results;
- adds header-only inline methods when no symbol method with the same normalized signature exists;
- recursively copies base declarations, their layouts, and their fields.

For the 1.21.3 slice, `Actor` gets size 1096, base `ActorStatusProvider`, and its `filler[1088]` field; `Mob : Actor`, `Player : Mob`; `BlockPos` gets size 12 and base `ll::math::intN3<BlockPos>`, whose flattened fields `x`/`y`/`z` sit at 0/32/64. The header layout is written to both configurations; for these plain-int value types the layout is identical on Windows and Linux, but virtual/inherited classes would need per-configuration confirmation. `Actor`'s generated header collapses its instance fields into `filler`, so individual Actor fields are not recoverable from this source.

## Symbol dispatch

`emit --symbol-dispatch` consumes this IR without any game headers. Direct calls resolve the module base plus `rva` (Windows) or the link-time `address` (Linux), falling back to `GetProcAddress`/`dlsym` on the recorded `symbol`; virtual calls index the per-configuration `vtableIndex`. Pass `--target-module <name>` when the symbols live in a module other than `bedrock_server`.

```powershell
./frontend/run-symbols.ps1 -Pdb <pdb> -Exe <exe> -Elf <debug> -IrOutput artifacts/symbol-ir/ir -NoPrototype
dotnet run --project ../src/Hosihikari.Binding.Generator/Hosihikari.Binding.Generator.csproj -c Release -- `
  emit artifacts/symbol-ir/ir/ir-manifest.json artifacts/symbol-ir/ir artifacts/symbol-dispatch `
  --abi-header ../../Binding.Abi/native/hosihikari_abi.h `
  --symbol-dispatch --target-module bedrock_server
```

The C# projection is unchanged. `tests/symbol-dispatch/run.ps1` builds a synthetic PE/PDB fixture, generates its IR, emits the dispatch shim, and runs `tests/SymbolDispatchSmoke`; `tests/fixtures/symbol-dispatch` covers the Linux resolver and Itanium vtable offset in CI. `tests/bds-smoke` deploys the shim to a real 1.21.3 LeviLamina server and calls the managed `BlockPos` projection from a CLR hosted inside the server process.

## Next steps

- Validate class/field facts against LeviLamina headers as a supplement; symbols remain the primary input.
- Extend symbol dispatch beyond scalars, strings, single-level pointers, offset-based fields, and trivially destructible by-value records: containers, optional/shared values, and non-trivial record returns need new layout and ABI facts.
- Broaden the class set (Level, ItemStack, Vec3) and record per-configuration coverage.
