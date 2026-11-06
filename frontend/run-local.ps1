param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$LeviLaminaRoot = "",
    [string]$Output = "artifacts/llapi-ir"
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) {
    throw "Visual Studio with the C++ toolchain was not found."
}

$Msvc = Get-ChildItem (Join-Path $VsRoot "VC\Tools\MSVC") | Sort-Object Name | Select-Object -Last 1
$SdkRoot = "C:\Program Files (x86)\Windows Kits\10\Include"
$Sdk = Get-ChildItem $SdkRoot | Sort-Object Name | Select-Object -Last 1
$ClangResource = Get-ChildItem (Join-Path $LlvmRoot "lib\clang") | Sort-Object Name | Select-Object -Last 1

$CompilerArgs = @(
    "-resource-dir", (Join-Path $LlvmRoot "lib\clang\$($ClangResource.Name)"),
    "-fms-extensions",
    "-fms-compatibility",
    "-fms-compatibility-version=19.44",
    "-isystem", (Join-Path $Msvc.FullName "include"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\um")
)

Push-Location (Join-Path $PSScriptRoot "..")
try {
    $Exe = Join-Path (Get-Location) "build\frontend\Release\hosihikari-ir-frontend.exe"
    $env:PATH = "$(Join-Path $LlvmRoot 'bin');$env:PATH"

    & $Exe `
        --surface levilamina `
        --output $Output `
        --root (Join-Path $LeviLaminaRoot "src") `
        --filter (Join-Path $LeviLaminaRoot "src\ll\api") `
        --configuration win-x64-server `
        --header ll/api/event/Event.h `
        --module ll/api/event `
        -- @CompilerArgs

    if ($LASTEXITCODE -ne 0) {
        throw "frontend exited with $LASTEXITCODE"
    }

    dotnet run --project src/Hosihikari.Bindings.Generator/Hosihikari.Bindings.Generator.csproj -c Release -- validate-ir "$Output/ir-manifest.json" "$Output"
}
finally {
    Pop-Location
}
