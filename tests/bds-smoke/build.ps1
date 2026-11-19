param(
    [Parameter(Mandatory = $true)]
    [string]$ServerRoot,
    [Parameter(Mandatory = $true)]
    [string]$IrManifest,
    [string]$IrDirectory = "",
    [string]$Output = "artifacts/bds-smoke"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ServerRoot = (Resolve-Path $ServerRoot).Path
$IrManifest = (Resolve-Path $IrManifest).Path
if ([string]::IsNullOrWhiteSpace($IrDirectory)) {
    $IrDirectory = Split-Path $IrManifest -Parent
}
$IrDirectory = (Resolve-Path $IrDirectory).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $RepoRoot $Output }
$PluginSource = Join-Path $Output "plugin"

if (-not (Test-Path (Join-Path $ServerRoot "bedrock_server_mod.exe"))) {
    throw "bedrock_server_mod.exe was not found under $ServerRoot. Install LeviLamina and run PeEditor first."
}

$AbiHeader = Join-Path $RepoRoot "..\Binding.Abi\native\hosihikari_abi.h"
if (-not (Test-Path $AbiHeader)) { throw "hosihikari_abi.h was not found under ..\Binding.Abi." }

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) { throw "Visual Studio with the C++ toolchain was not found." }
$DevCmd = Join-Path $VsRoot "Common7\Tools\VsDevCmd.bat"
function Invoke-Msvc([string]$CommandLine) {
    & cmd.exe /c "`"$DevCmd`" -arch=x64 -host_arch=x64 -no_logo >nul 2>&1 && $CommandLine"
    if ($LASTEXITCODE -ne 0) { throw "MSVC command failed: $CommandLine" }
}

$HostPackRoot = Join-Path $env:ProgramFiles "dotnet\packs\Microsoft.NETCore.App.Host.win-x64"
$HostPack = Get-ChildItem $HostPackRoot -Directory |
    Where-Object { $_.Name -match '^10\.' } |
    Sort-Object { [version]$_.Name } |
    Select-Object -Last 1
if (-not $HostPack) { throw "Microsoft.NETCore.App.Host.win-x64 10.x was not found." }
$HostNative = Join-Path $HostPack.FullName "runtimes\win-x64\native"
if (-not (Test-Path (Join-Path $HostNative "hostfxr.h"))) { throw "hostfxr headers were not found under $HostNative." }

Push-Location $RepoRoot
try {
    dotnet run --project (Join-Path $RepoRoot "src\Hosihikari.Binding.Generator\Hosihikari.Binding.Generator.csproj") -c Release -- `
        emit $IrManifest $IrDirectory $Output `
        --abi-header $AbiHeader `
        --symbol-dispatch --target-module bedrock_server_mod.exe

    $ShimSource = Join-Path $Output "shim\win-x64-server\bedrock\binding_table.cpp"
    $ShimDll = Join-Path $Output "symbol-shim.dll"
    Invoke-Msvc "cl /nologo /std:c++20 /EHsc /MD /O2 /bigobj /LD `"$ShimSource`" /I `"$(Split-Path $ShimSource -Parent)`" /Fo:`"$Output\\`" /Fd:`"$Output\\shim-cl.pdb`" /Fe:`"$ShimDll`""

    dotnet build (Join-Path $PSScriptRoot "managed\Hosihikari.SymbolSmoke.csproj") -c Release --nologo -v:q `
        -p:InteropDirectory=$Output/interop
    if ($LASTEXITCODE -ne 0) { throw "managed smoke build failed with $LASTEXITCODE" }

    $ManagedOutput = Join-Path $PSScriptRoot "managed\bin\Release\net10.0"
    New-Item -ItemType Directory -Force -Path $PluginSource | Out-Null
    Get-ChildItem $PluginSource -File | Remove-Item -Force
    Get-ChildItem $ManagedOutput -File | Where-Object { $_.Extension -in ".dll", ".json" } |
        Copy-Item -Destination $PluginSource -Force
    Copy-Item (Join-Path $HostNative "nethost.dll") $PluginSource -Force
    Copy-Item (Join-Path $PSScriptRoot "plugin\manifest.json") $PluginSource -Force
    Copy-Item $ShimDll $PluginSource -Force

    $PluginDll = Join-Path $PluginSource "hosihikari-symbol-smoke.dll"
    Invoke-Msvc "cl /nologo /std:c++20 /EHsc /MD /O2 /LD `"$(Join-Path $PSScriptRoot 'plugin\main.cpp')`" /I `"$HostNative`" /Fo:`"$PluginSource\\`" /Fd:`"$PluginSource\\plugin-cl.pdb`" /Fe:`"$PluginDll`""

    $PluginTarget = Join-Path $ServerRoot "plugins\hosihikari-symbol-smoke"
    if (Test-Path $PluginTarget) { Remove-Item -Recurse -Force $PluginTarget }
    New-Item -ItemType Directory -Force -Path $PluginTarget | Out-Null
    Copy-Item (Join-Path $PluginSource "*") $PluginTarget -Recurse -Force

    Write-Output "Built $PluginDll"
    Write-Output "Deployed $PluginTarget"
}
finally {
    Pop-Location
}
