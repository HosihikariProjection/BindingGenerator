param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$EndstoneRoot = "",
    [string]$Output = "artifacts/endstone-ir"
)
if ([string]::IsNullOrWhiteSpace($EndstoneRoot)) {
    $EndstoneRoot = Join-Path $PSScriptRoot "..\..\Third\Endstone"
}

$ErrorActionPreference = "Stop"

$GeneratorRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$EndstoneRoot = (Resolve-Path $EndstoneRoot).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $GeneratorRoot $Output }
$IncludeRoot = Join-Path $EndstoneRoot "include"

$VersionFile = Join-Path $IncludeRoot "CMakeLists.txt"
$Version = "0.12.0"
if (Test-Path $VersionFile) {
    $Match = Select-String -Path $VersionFile -Pattern 'project\s*\(\s*endstone\s+VERSION\s+([0-9]+\.[0-9]+\.[0-9]+)' | Select-Object -First 1
    if ($Match) { $Version = $Match.Matches[0].Groups[1].Value }
}

$GeneratedRoot = Join-Path $GeneratorRoot "artifacts\endstone-generated\include"
$GeneratedHeader = Join-Path $GeneratedRoot "endstone\version.h"
New-Item -ItemType Directory -Force -Path (Split-Path $GeneratedHeader -Parent) | Out-Null
$Parts = $Version.Split('.')
$VersionHeader = @"
#pragma once
#define ENDSTONE_VERSION_MAJOR $($Parts[0])
#define ENDSTONE_VERSION_MINOR $($Parts[1])
#define ENDSTONE_VERSION_PATCH $($Parts[2])
#define ENDSTONE_API_VERSION "$($Parts[0]).$($Parts[1])"
#ifndef ENDSTONE_VERSION
#define ENDSTONE_VERSION "$Version"
#endif
"@
[System.IO.File]::WriteAllText($GeneratedHeader, $VersionHeader, (New-Object System.Text.UTF8Encoding($false)))

$ExpectedLite = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\e\expected-lite" -Recurse -Filter expected.hpp -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $ExpectedLite) { throw "expected-lite headers were not found." }
$Nlohmann = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\n\nlohmann_json" -Recurse -Filter json.hpp -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $Nlohmann) { throw "nlohmann_json headers were not found." }

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) { throw "Visual Studio with the C++ toolchain was not found." }

$Msvc = Get-ChildItem (Join-Path $VsRoot "VC\Tools\MSVC") | Sort-Object Name | Select-Object -Last 1
$SdkRoot = "C:\Program Files (x86)\Windows Kits\10\Include"
$Sdk = Get-ChildItem $SdkRoot | Sort-Object Name | Select-Object -Last 1
$ClangResource = Get-ChildItem (Join-Path $LlvmRoot "lib\clang") | Sort-Object Name | Select-Object -Last 1

$CompilerArgs = @(
    "-std=c++20",
    "-resource-dir", (Join-Path $LlvmRoot "lib\clang\$($ClangResource.Name)"),
    "-fms-extensions",
    "-fms-compatibility",
    "-fms-compatibility-version=19.44",
    "-DNOMINMAX",
    "-DWIN32_LEAN_AND_MEAN",
    "-I", $IncludeRoot,
    "-I", (Join-Path $IncludeRoot "endstone"),
    "-I", $GeneratedRoot,
    "-I", (Split-Path (Split-Path $ExpectedLite.FullName -Parent) -Parent),
    "-I", (Split-Path (Split-Path $Nlohmann.FullName -Parent) -Parent),
    "-isystem", (Join-Path $Msvc.FullName "include"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\um")
)

Push-Location $EndstoneRoot
try {
    $Exe = Join-Path $GeneratorRoot "build\frontend\Release\hosihikari-ir-frontend.exe"
    $env:PATH = "$(Join-Path $LlvmRoot 'bin');$env:PATH"

    & $Exe `
        --surface endstone `
        --output $Output `
        --root $IncludeRoot `
        --filter (Join-Path $IncludeRoot "endstone") `
        --configuration win-x64-server `
        --module endstone `
        --source (Join-Path $IncludeRoot "endstone\endstone.hpp") `
        -- @CompilerArgs

    if ($LASTEXITCODE -ne 0) {
        throw "frontend exited with $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

dotnet run --project (Join-Path $GeneratorRoot "src\Hosihikari.Bindings.Generator\Hosihikari.Bindings.Generator.csproj") -c Release -- validate-ir "$Output/ir-manifest.json" "$Output"
