param(
    [Parameter(Mandatory = $true)]
    [string]$IrDirectory,
    [string]$Output = "artifacts/llapi-real-shim",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$LeviLaminaRoot = "",
    [switch]$LinkCarrier,
    [string]$CarrierImportLibrary = "",
    [string]$AbiRoot = "",
    [string[]]$AdditionalLinkLibrary = @(),
    [switch]$SkipRun
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$GeneratorRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$IrDirectory = (Resolve-Path $IrDirectory).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $GeneratorRoot $Output }
$Manifest = Join-Path $IrDirectory "ir-manifest.json"
if (-not (Test-Path $Manifest)) {
    throw "ir-manifest.json was not found under $IrDirectory."
}

$AbiRoot = if ([string]::IsNullOrWhiteSpace($AbiRoot)) {
    Join-Path (Split-Path $GeneratorRoot -Parent) "Abi"
} else {
    (Resolve-Path $AbiRoot).Path
}
$AbiHeader = Join-Path $AbiRoot "native\hosihikari_abi.h"
if (-not (Test-Path $AbiHeader)) {
    throw "hosihikari_abi.h was not found under $AbiRoot."
}

$EmitArgs = @("--native-root", $LeviLaminaRoot, "--abi-header", $AbiHeader)
if ($LinkCarrier) {
    $EmitArgs += "--link-carrier"
    $EmitArgs += "--ll-allocator"
}

$GeneratorProject = Join-Path $GeneratorRoot "src\Hosihikari.Bindings.Generator\Hosihikari.Bindings.Generator.csproj"
dotnet run --project $GeneratorProject -c Release -- emit $Manifest $IrDirectory $Output @EmitArgs

$NativeDirectory = Join-Path $Output "native"
New-Item -ItemType Directory -Force -Path $NativeDirectory | Out-Null
$Dll = Join-Path $NativeDirectory "Hosihikari.Bindings.LeviLamina.Shim.dll"

$ShimSource = Get-ChildItem (Join-Path $Output "shim") -Recurse -Filter binding_table.cpp |
    Where-Object { $_.FullName -match 'levilamina' } |
    Select-Object -First 1
if (-not $ShimSource) {
    throw "No levilamina binding_table.cpp was emitted under $Output."
}

$FmtLib = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\f\fmt" -Recurse -Filter fmt.lib -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($LinkCarrier) {
    if ([string]::IsNullOrWhiteSpace($CarrierImportLibrary)) {
        $CarrierImportLibrary = Join-Path $LeviLaminaRoot "build\windows\x64\release\LeviLamina.lib"
    }

    if (-not (Test-Path $CarrierImportLibrary)) {
        throw "The carrier import library was not found at $CarrierImportLibrary."
    }

    $CarrierArgs = @{
        ShimDirectory = $ShimSource.Directory.FullName
        OutputDll = $Dll
        LlvmRoot = $LlvmRoot
        LeviLaminaRoot = $LeviLaminaRoot
        CarrierImportLibrary = $CarrierImportLibrary
        AdditionalLinkLibrary = $AdditionalLinkLibrary
        DelayLoad = $true
    }
    if ($FmtLib) {
        $CarrierArgs.FmtLib = $FmtLib.FullName
    }

    & (Join-Path $PSScriptRoot "build-carrier-shim.ps1") @CarrierArgs
} else {
    $LinkLibraries = @($AdditionalLinkLibrary)
    if ($FmtLib) {
        $LinkLibraries += $FmtLib.FullName
    }

    $BuildArgs = @{
        ShimDirectory = $ShimSource.Directory.FullName
        OutputDll = $Dll
        LlvmRoot = $LlvmRoot
        LeviLaminaRoot = $LeviLaminaRoot
    }
    if ($LinkLibraries.Count -gt 0) {
        $BuildArgs.AdditionalLinkLibrary = $LinkLibraries
    }

    & (Join-Path $PSScriptRoot "build-shim.ps1") @BuildArgs
}

if ($SkipRun) {
    Write-Output "Built $Dll (smoke run skipped)."
    return
}

$SmokeProject = Join-Path $GeneratorRoot "tests\BindingShimSmoke\Smoke.csproj"
dotnet run --project $SmokeProject -c Release "-p:InteropDirectory=$(Join-Path $Output 'interop')" -- $Dll
