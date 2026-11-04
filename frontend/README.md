# libTooling frontend

The native frontend parses the three header sets and emits the versioned IR defined in `HosihikariProjection/Design` (`docs/ir-and-coverage.md`). It is a separate CMake target because libTooling has no stable C++ API and cannot be hosted in-process by .NET 10.

## Toolchain

Pin LLVM/Clang **23.1.4**.

- **Windows**: the LLVM MSI is toolchain-only and does not contain `clang/Tooling` headers, `clangTooling.lib` or `ClangConfig.cmake`. Use the official `clang+llvm-23.1.4-x86_64-pc-windows-msvc.tar.xz` (or build LLVM), and point `CMAKE_PREFIX_PATH` at the extracted tree.
- **Linux**: use the official `LLVM-23.1.4-Linux-X64.tar.xz`. Distribution `libclang-23-dev` may be a different patch level; do not mix.

## Build

```powershell
./frontend/build.ps1 -LlvmRoot <llvm-root>
```

```shell
./frontend/build.sh --llvm-root <llvm-root>
```

The Windows script expects the extracted `clang+llvm-23.1.4-x86_64-pc-windows-msvc` tree and uses the Visual Studio 18 generator. The Linux script uses Ninja when available and builds `build/frontend-linux/hosihikari-ir-frontend`. A manual CMake invocation works too:

```shell
cmake -S frontend -B build/frontend -G "Visual Studio 18 2026" -A x64 -DCMAKE_PREFIX_PATH=<llvm-root>
cmake --build build/frontend --config Release
```

## Run

```powershell
./frontend/run-local.ps1 -LlvmRoot <llvm-root> -LeviLaminaRoot <checkout>
./frontend/run-umbrella.ps1 -LlvmRoot <llvm-root> -LeviLaminaRoot <checkout> -Module ll/api
```

`run-local.ps1` runs the frontend on `ll/api/event/Event.h`. `run-umbrella.ps1` builds an umbrella from the module's headers, reuses the include paths and defines from the LeviLamina `compile_commands.json`, and validates the produced IR with the .NET CLI.

For the Endstone Linux configuration:

```shell
./frontend/run-endstone.sh \
  --endstone-root <checkout> \
  --llvm-root <llvm-root> \
  --expected-lite-include <expected-lite/include> \
  --nlohmann-include <nlohmann-json/include> \
  --output artifacts/endstone-linux-ir
```

The script emits `linux-x64-server` IR and validates it with the .NET CLI. On libstdc++, `--compat-include` can force-include headers such as the Endstone `std::floorf` compatibility shim from `HosihikariProjection/EndstoneBinding`.

## Symbol prototype (Bedrock 1.21.3)

`hosihikari-symbol-ir-frontend` reads a publics-only PDB and an optional ELF symbol table, demangles a selected set of classes, merges Windows return types into the Linux records, and reconstructs vtable slots. `--output` writes a prototype JSON; `--ir-output` writes formal IR 1.5.1 shards and a manifest with `symbol`, `address`, `rva`, and per-configuration vtable records. Omitting `--elf` emits the Windows configuration alone, which the synthetic fixture uses. See `symbols/README.md`.

```powershell
./frontend/run-symbols.ps1 -Pdb <pdb> -Exe <pe> -Elf <debug> -Classes Actor,BlockPos
```

`frontend/run-levilamina-headers.ps1` generates a header supplement from a LeviLamina v0.13.5 checkout targeting BDS 1.21.3.01. It passes `--flatten-anonymous-fields`, which emits fields nested in anonymous unions/structs with absolute bit offsets; the flag is off by default so existing IRs stay unchanged. `frontend/merge-symbol-ir.py` then merges fields, layouts, inheritance, and inline methods from that IR into the symbol IR.

`frontend/extract-class-sizes.py` recovers `sizeof` for every class the carrier deletes through a virtual destructor. A class with one gets a `??_G…` thunk that calls the sized `operator delete` with `sizeof(T)` as the immediate; reading that immediate per thunk is exact and needs no type stream. On the 1.21.3 server it yields 18,386 classes, and every class whose header layout the annotation check below accepts agrees with it.

The merge prefers that size. A header layout is otherwise merged only when it is provably the real one: the class declares its own data members, clang's offsets for them match the `// this+0x…` offsets the header generator recorded, and every base class passes the same test. The generated `mc/` headers drop the members their generator did not recover, so an incomplete class still parses — as an empty class, or as one carrying nothing but its vtable and bases — and clang then lays it out far below its real size. `EatBlockGoal` was such a class: the header gave it the 8 bytes of its base while the real object is 264 bytes, so allocating that would corrupt the heap. Field offsets are kept only when the proven layout describes the same size as the recovered one, because an offset computed from an incomplete member list points at the wrong storage. `frontend/layout-trust.txt` lists the few types whose data lives entirely in a base class, which pass no test of their own but are known to be complete.

`--header-root` re-roots the source paths recorded in a header IR, so an IR built in a checkout that has since moved still finds the annotations it validates against; a missing source is treated as unproven, never as complete.

`frontend/headers/mc-26.51.7.txt` maps the same `mc/` slice to the LeviLamina 26.51.7 checkout. `-UseCompileCommands` reuses the checkout's server compile environment, and `-I src-server` precedes the `src` overlay. The probe yields 558 declarations / 7,367 members / 5,325 BindingShim functions at IR 1.5.1. A compile pass against the same headers reports 36 errors, concentrated in `brstd` wrappers, `ll::math` template instances, `PathBuffer`, and callback overloads; the remaining work is type-specific.

## API availability markers

`frontend/api-whitelist.py` classifies a LeviLamina `mc/` header tree by its availability markers. `MCAPI` marks a declaration the runtime exports, `MCFOLD` an exported symbol subject to identical COMDAT folding, and `MCTAPI` an explicit template specialisation. `MCNAPI` is `[[deprecated]] MCAPI`: the declaration is known but the runtime does not expose it. The non-`MCNAPI` set is the usable interop surface of the shipped server and the whitelist a binding can target.

```powershell
python frontend/api-whitelist.py --root <levilamina-26.51-checkout> --output artifacts/mcapi-26.51
```

For the 26.51.7 checkout it yields 39,319 usable markers (32,817 `MCAPI`, 6,476 `MCFOLD`, 26 `MCTAPI`) against 18,683 `MCNAPI` markers, spread over 6,043 scopes in 5,888 headers. 2,199 scopes carry at least five usable markers and account for 79.9% of them, so `mcapi-scopes.tsv` ranks the scopes worth adding to a header slice. The default `--sources` covers `src/mc` and the `src-server/mc` overlay; `src-client/mc` stays out of the server whitelist. Client-only sections inside `src/mc` are guarded by `LL_PLAT_C`, which the `client_only` column flags; those are 6.5% of the usable markers.

## Class candidates

`frontend/class-candidates.py` ranks what the selected class list still leaves out. Symbol discovery reports RTTI spellings, not declarations — `VBlockDescriptor` carries the kind letter, `ScriptActor::ScriptModuleMinecraft` reverses the namespace — so the script resolves each spelling against the header tree and keeps only types that have both a symbol and a declaration. Library namespaces are dropped.

```powershell
python frontend/class-candidates.py --root <levilamina-checkout> `
  --classes artifacts/symbol-classes-1.21.3.tsv `
  --selected ../Binding.Bedrock/scripts/classes-1.21.3.txt `
  --output artifacts/class-candidates-1.21.3.tsv
```

Against the v0.13.5 checkout, 7,108 header types match 5,967 discovered names; 4,608 types remain unselected and carry 32,892 symbol hits. 273 of them reach at least 20 hits, which is the size the next batch is drawn from. The counts rank candidates; discovery spellings overlap, so they are not a coverage figure.

The 26.51 server binaries are the only other candidate source, and they are stripped: `.dynsym` holds 392 entries, there is no `.symtab`, and `.gnu_debuglink` names a file Mojang does not publish. The markers in the generated headers are therefore the availability record for that version.

Spot check: the prelink output of the existing carrier builds carries 74 runtime imports, and every one that maps to a header declaration is usable — 49 rows, none `MCNAPI`. The remainder are a helper template declared without a marker and names the matcher does not resolve.

The generator's `emit --symbol-dispatch` mode turns that IR into a shim that does not include game headers: direct calls use the module base plus `rva`/`address` (or `GetProcAddress`/`dlsym` on the recorded symbol), and virtual calls use the per-configuration `vtableIndex`. `tests/symbol-dispatch/run.ps1` exercises the Windows PE/PDB path end-to-end with a synthetic fixture; the `cli` CI job runs the Linux fixture and resolver smoke.

## Current state

The frontend parses the full `src/ll/api` umbrella plus the `src-server/ll/api` overlays (1,530 declarations / 6,145 members in the current full IR) and the smaller `ll/api/event` module. It emits JSONL IR plus `ir-manifest.json` and validates the result through the .NET CLI. The visitor covers classes/structs/unions, fields, static fields, methods, constructors, destructors, operators, enums, namespace-scope functions, and concrete template instantiations. Public record types referenced by value from filtered signatures are emitted with their constructors, destructor, and fields even when declared outside the module filter. Macro provenance is inferred from export/deprecated/nodiscard attributes; uninstantiated template declarations and partial specializations are skipped.

Type canonical strings are platform-neutral for identity purposes. On targets where `long` is 64-bit, canonical `long`/`unsigned long` spellings are rewritten to `long long`/`unsigned long long`, matching the Windows spelling of `std::int64_t`, `std::uint64_t`, and `size_t`. Declaration ids, table field hashes, and coverage therefore stay identical between `win-x64-server` and `linux-x64-server` IR.

## Full IR

```powershell
./frontend/run-ll-full.ps1 -Output artifacts/llapi-full-ir
```

`run-ll-full.ps1` wraps `run-umbrella.ps1 -Module ll/api`, which parses the complete `src/ll/api` plus `src-server/ll/api` tree in one translation unit and validates the result. Dependency enums declared outside the filter are emitted automatically.

## Dependency enums

Enums are often declared in headers outside a module filter while still appearing in filtered signatures. The visitor records such enums as dependency declarations after the main traversal, so the ABI planner can map their underlying integer type. When an IR is assembled from separately parsed modules, merge the missing declarations with:

```powershell
python frontend/merge-ir.py --base <base-ir> --supplement <module-ir> --output <merged-ir> --record-type declaration --kind enum
```

`merge-ir.py` copies only records matching the requested type/kind, skips ids already present, and recomputes the shard hash, record count, and manifest counts.

The frontend is not part of the .NET fixture job; it requires the pinned LLVM toolchain, zlib/zstd, and the MSVC/SDK or libstdc++ headers. The `endstone-linux-ir` CI job builds it on Linux, generates `linux-x64-server` IR from Endstone 0.11.14, emits from that IR, and compiles the resulting shim.

`build.ps1`, `run-local.ps1`, and `run-umbrella.ps1` cover the local Windows slice; `build.sh` and `run-endstone.sh` cover the Linux slice. `new-carrier.ps1` drives a whole carrier bump across the binding surfaces; see the repository README.
