param(
    [string]$Output = "artifacts/symbol-fixture",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $RepoRoot $Output }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) { throw "Visual Studio with the C++ toolchain was not found." }

$DevCmd = Join-Path $VsRoot "Common7\Tools\VsDevCmd.bat"
function Invoke-Msvc([string]$CommandLine) {
    & cmd.exe /c "`"$DevCmd`" -arch=x64 -host_arch=x64 -no_logo >nul 2>&1 && $CommandLine"
    if ($LASTEXITCODE -ne 0) { throw "MSVC command failed: $CommandLine" }
}

$Fixture = Join-Path $PSScriptRoot "fixture.cpp"
$FixtureDll = Join-Path $Output "fixture-bds.dll"
$FixturePdb = Join-Path $Output "fixture-bds.pdb"
$IrDirectory = Join-Path $Output "ir"
$DispatchDirectory = Join-Path $Output "dispatch"
$ShimDll = Join-Path $Output "hosihikari-fixture.dll"

Push-Location $RepoRoot
try {
    Invoke-Msvc "cl /nologo /LD /Od /Ob0 /Zi /EHsc /MD /std:c++20 `"$Fixture`" /Fo:`"$Output\\`" /Fd:`"$Output\\fixture-cl.pdb`" /link /DEBUG /OPT:NOREF /OPT:NOICF /INCREMENTAL:NO /OUT:`"$FixtureDll`" /PDB:`"$FixturePdb`""

    $symbolArguments = @{
        Pdb = $FixturePdb
        Exe = $FixtureDll
        NoPrototype = $true
        IrOutput = $IrDirectory
        Version = "fixture"
        Classes = @("FixtureBlock")
        LlvmRoot = $LlvmRoot
        SkipBuild = $SkipFrontendBuild
    }
    & (Join-Path $RepoRoot "frontend\run-symbols.ps1") @symbolArguments

    # The PDB carries signatures only. Merge the hand-authored header facts (FixturePair's
    # layout and trivially-destructible trait) so by-value record dispatch can be exercised.
    $HeaderFixture = Join-Path $RepoRoot "tests\fixtures\symbol-dispatch-header"
    $MergedDirectory = Join-Path $Output "ir-merged"
    & python (Join-Path $RepoRoot "frontend\merge-symbol-ir.py") `
        --symbol $IrDirectory `
        --header $HeaderFixture `
        --output $MergedDirectory `
        --classes FixtureBlock
    if ($LASTEXITCODE -ne 0) { throw "merge-symbol-ir.py exited with $LASTEXITCODE" }

    $AbiHeader = Join-Path $RepoRoot "..\Binding.Abi\native\hosihikari_abi.h"
    if (-not (Test-Path $AbiHeader)) { throw "hosihikari_abi.h was not found under ..\Binding.Abi." }

    dotnet run --project (Join-Path $RepoRoot "src\Hosihikari.Bindings.Generator\Hosihikari.Bindings.Generator.csproj") -c Release -- `
        emit (Join-Path $MergedDirectory "ir-manifest.json") $MergedDirectory $DispatchDirectory `
        --abi-header $AbiHeader `
        --symbol-dispatch --target-module "fixture-bds.dll"

    $ShimSource = Join-Path $DispatchDirectory "shim\win-x64-server\bedrock\binding_table.cpp"
    Invoke-Msvc "cl /nologo /std:c++20 /EHsc /utf-8 /MD /LD `"$ShimSource`" /I `"$(Split-Path $ShimSource -Parent)`" /Fo:`"$Output\\`" /Fd:`"$Output\\shim-cl.pdb`" /Fe:`"$ShimDll`""

    dotnet run --project (Join-Path $RepoRoot "tests\SymbolDispatchSmoke\SymbolDispatchSmoke.csproj") -c Release -- $FixtureDll $ShimDll
}
finally {
    Pop-Location
}
