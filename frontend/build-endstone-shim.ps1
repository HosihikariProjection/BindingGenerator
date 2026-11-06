param(
    [Parameter(Mandatory = $true)]
    [string]$ShimDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDll,
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$EndstoneRoot = "",
    [string[]]$AdditionalIncludePath = @()
)
if ([string]::IsNullOrWhiteSpace($EndstoneRoot)) {
    $EndstoneRoot = Join-Path $PSScriptRoot "..\..\Third\Endstone"
}

$ErrorActionPreference = "Stop"

$GeneratorRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ShimDirectory = (Resolve-Path $ShimDirectory).Path
$OutputDll = [System.IO.Path]::GetFullPath($OutputDll)
$EndstoneRoot = (Resolve-Path $EndstoneRoot).Path
$IncludeRoot = Join-Path $EndstoneRoot "include"
$GeneratedRoot = Join-Path $GeneratorRoot "artifacts\endstone-generated\include"
New-Item -ItemType Directory -Force -Path (Split-Path $OutputDll -Parent) | Out-Null

$Source = Join-Path $ShimDirectory "binding_table.cpp"
if (-not (Test-Path $Source)) {
    throw "binding_table.cpp was not found under $ShimDirectory."
}

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

$Args = @(
    "/std:c++20",
    "/EHsc",
    "/DNOMINMAX",
    "/DWIN32_LEAN_AND_MEAN",
    "/I", $IncludeRoot,
    "/I", (Join-Path $IncludeRoot "endstone"),
    "/I", $GeneratedRoot,
    "/I", (Split-Path (Split-Path $ExpectedLite.FullName -Parent) -Parent),
    "/I", (Split-Path (Split-Path $Nlohmann.FullName -Parent) -Parent),
    "/imsvc", (Join-Path $Msvc.FullName "include"),
    "/imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
    "/imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
    "/imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\um"),
    "/MD",
    "/LD",
    $Source,
    "/Fe:$OutputDll"
)

foreach ($include in $AdditionalIncludePath) {
    $Args += @("/I", (Resolve-Path $include).Path)
}

$ClangCl = Join-Path $LlvmRoot "bin\clang-cl.exe"
& $ClangCl @Args
if ($LASTEXITCODE -ne 0) {
    throw "clang-cl exited with $LASTEXITCODE"
}

Write-Output "Built $OutputDll"
