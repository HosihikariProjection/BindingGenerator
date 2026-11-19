#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: run-endstone.sh --endstone-root <dir> --llvm-root <dir> \
         --expected-lite-include <dir> --nlohmann-include <dir> [options]

Options:
  --endstone-root <dir>           Endstone checkout containing include/endstone (required).
  --llvm-root <dir>               LLVM/Clang 23 install tree (required unless --frontend is set).
  --frontend <file>               Frontend executable. Default: build/frontend-linux/hosihikari-ir-frontend.
  --expected-lite-include <dir>   Directory containing nonstd/expected.hpp (required).
  --nlohmann-include <dir>        Directory containing nlohmann/json.hpp (required).
  --compat-include <file>         Header force-included into the translation unit.
  --output <dir>                  IR output directory. Default: artifacts/endstone-linux-ir.
  --configuration <id>            Configuration id. Default: linux-x64-server.
  --skip-validate                 Do not run the .NET IR validator.
  -h, --help                      Show this help.
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_root/.." && pwd)"
endstone_root=""
llvm_root=""
frontend=""
expected_lite_include=""
nlohmann_include=""
compat_include=""
output="$repo_root/artifacts/endstone-linux-ir"
configuration="linux-x64-server"
validate=1

while [ "$#" -gt 0 ]; do
    case "$1" in
        --endstone-root)
            endstone_root="${2:-}"
            shift 2
            ;;
        --llvm-root)
            llvm_root="${2:-}"
            shift 2
            ;;
        --frontend)
            frontend="${2:-}"
            shift 2
            ;;
        --expected-lite-include)
            expected_lite_include="${2:-}"
            shift 2
            ;;
        --nlohmann-include)
            nlohmann_include="${2:-}"
            shift 2
            ;;
        --compat-include)
            compat_include="${2:-}"
            shift 2
            ;;
        --output)
            output="${2:-}"
            shift 2
            ;;
        --configuration)
            configuration="${2:-}"
            shift 2
            ;;
        --skip-validate)
            validate=0
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [ -z "$endstone_root" ] || [ -z "$expected_lite_include" ] || [ -z "$nlohmann_include" ]; then
    usage >&2
    exit 2
fi
if [ -z "$frontend" ] && [ -z "$llvm_root" ]; then
    usage >&2
    exit 2
fi

endstone_root="$(cd "$endstone_root" && pwd)"
expected_lite_include="$(cd "$expected_lite_include" && pwd)"
nlohmann_include="$(cd "$nlohmann_include" && pwd)"
include_root="$endstone_root/include"
frontend="${frontend:-$repo_root/build/frontend-linux/hosihikari-ir-frontend}"
if [ ! -x "$frontend" ]; then
    echo "Frontend executable was not found: $frontend" >&2
    exit 1
fi
if [ ! -f "$include_root/endstone/endstone.hpp" ]; then
    echo "Endstone headers were not found under $include_root." >&2
    exit 1
fi
if [ ! -f "$expected_lite_include/nonstd/expected.hpp" ]; then
    echo "expected-lite headers were not found under $expected_lite_include." >&2
    exit 1
fi
if [ ! -f "$nlohmann_include/nlohmann/json.hpp" ]; then
    echo "nlohmann_json headers were not found under $nlohmann_include." >&2
    exit 1
fi
output="$(mkdir -p "$output" && cd "$output" && pwd)"

resource_args=()
if [ -n "$llvm_root" ]; then
    llvm_root="$(cd "$llvm_root" && pwd)"
    clang_lib_root="$llvm_root/lib/clang"
    if [ ! -d "$clang_lib_root" ]; then
        clang_lib_root="$llvm_root/lib64/clang"
    fi
    resource_dir="$(find "$clang_lib_root" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -n 1)"
    if [ -n "$resource_dir" ]; then
        resource_args=(-resource-dir "$resource_dir")
    fi
fi

compat_args=()
if [ -n "$compat_include" ]; then
    if [ ! -f "$compat_include" ]; then
        echo "Compatibility header was not found: $compat_include" >&2
        exit 1
    fi
    compat_args=(-include "$(cd "$(dirname "$compat_include")" && pwd)/$(basename "$compat_include")")
fi

version="$(sed -n 's/.*project[[:space:]]*([[:space:]]*endstone[[:space:]]*VERSION[[:space:]]*\([0-9][0-9.]*\).*/\1/p' \
    "$include_root/CMakeLists.txt" | head -n 1)"
version="${version:-0.11.14}"
IFS='.' read -r major minor patch <<EOF
$version
EOF
major="${major:-0}"
minor="${minor:-0}"
patch="${patch:-0}"

generated_root="$repo_root/artifacts/endstone-generated/include"
mkdir -p "$generated_root/endstone"
cat > "$generated_root/endstone/version.h" <<EOF
#pragma once
#define ENDSTONE_VERSION_MAJOR $major
#define ENDSTONE_VERSION_MINOR $minor
#define ENDSTONE_VERSION_PATCH $patch
#define ENDSTONE_API_VERSION "$major.$minor"
#ifndef ENDSTONE_VERSION
#define ENDSTONE_VERSION "$version"
#endif
EOF

pushd "$endstone_root" >/dev/null
trap 'popd >/dev/null' EXIT

"$frontend" \
    --surface endstone \
    --output "$output" \
    --root "$include_root" \
    --filter "$include_root/endstone" \
    --configuration "$configuration" \
    --module endstone \
    --source "$include_root/endstone/endstone.hpp" \
    -- \
    -std=c++20 \
    "${resource_args[@]}" \
    "${compat_args[@]}" \
    -I "$include_root" \
    -I "$include_root/endstone" \
    -I "$generated_root" \
    -I "$expected_lite_include" \
    -I "$nlohmann_include"

popd >/dev/null
trap - EXIT

if [ "$validate" -eq 1 ]; then
    dotnet run --project "$repo_root/src/Hosihikari.Binding.Generator/Hosihikari.Binding.Generator.csproj" \
        -c Release -- validate-ir "$output/ir-manifest.json" "$output"
fi
