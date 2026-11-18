param(
    [string]$EndstoneRoot = "",
    [string]$GeneratorRoot = ""
)
# Writes the synthetic endstone/version.h that build-endstone-shim.ps1 expects on its include
# path. The shim compile needs that header, but only the IR frontend script ever wrote it, so a
# build job that starts from an already-committed surface had no way to produce it.
#
# Kept separate from run-endstone.ps1 so it can run without LLVM, a frontend build or a parse.

$ErrorActionPreference = "Stop"

$GeneratorRoot = if ([string]::IsNullOrWhiteSpace($GeneratorRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    (Resolve-Path $GeneratorRoot).Path
}
$EndstoneRoot = if ([string]::IsNullOrWhiteSpace($EndstoneRoot)) {
    (Resolve-Path (Join-Path $GeneratorRoot "..\Third\Endstone")).Path
} else {
    (Resolve-Path $EndstoneRoot).Path
}

$IncludeRoot = Join-Path $EndstoneRoot "include"
$VersionFile = Join-Path $IncludeRoot "CMakeLists.txt"
if (-not (Test-Path $VersionFile)) {
    throw "The Endstone header root has no include/CMakeLists.txt: $EndstoneRoot"
}

$Version = "0.12.0"
$Match = Select-String -Path $VersionFile -Pattern 'project\s*\(\s*endstone\s+VERSION\s+([0-9]+\.[0-9]+\.[0-9]+)' | Select-Object -First 1
if ($Match) {
    $Version = $Match.Matches[0].Groups[1].Value
} else {
    Write-Warning "No project(endstone VERSION x.y.z) in $VersionFile; falling back to $Version."
}

$GeneratedHeader = Join-Path $GeneratorRoot "artifacts\endstone-generated\include\endstone\version.h"
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

Write-Output "Wrote $GeneratedHeader for Endstone $Version."
