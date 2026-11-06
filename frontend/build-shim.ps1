param(
    [Parameter(Mandatory = $true)]
    [string]$ShimDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDll,
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$LeviLaminaRoot = "",
    [string[]]$PrependIncludePath = @(),
    [string[]]$AdditionalIncludePath = @(),
    [string[]]$AdditionalLinkLibrary = @(),
    [string[]]$DelayLoadLibrary = @(),
    [int]$ErrorLimit = 0,
    [switch]$CompileOnly
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$ShimDirectory = (Resolve-Path $ShimDirectory).Path
$OutputDll = [System.IO.Path]::GetFullPath($OutputDll)
$Source = Join-Path $ShimDirectory "binding_table.cpp"
if (-not (Test-Path $Source)) {
    throw "binding_table.cpp was not found under $ShimDirectory."
}

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) {
    throw "Visual Studio with the C++ toolchain was not found."
}

$Msvc = Get-ChildItem (Join-Path $VsRoot "VC\Tools\MSVC") | Sort-Object Name | Select-Object -Last 1
$SdkRoot = "C:\Program Files (x86)\Windows Kits\10\Include"
$Sdk = Get-ChildItem $SdkRoot | Sort-Object Name | Select-Object -Last 1

$CompileCommands = Get-Content (Join-Path $LeviLaminaRoot "compile_commands.json") -Raw | ConvertFrom-Json
$Entry = $CompileCommands | Where-Object { $_.file -match 'src[\\/]ll[\\/]api' } | Select-Object -First 1
if (-not $Entry) {
    throw "No ll/api entry found in compile_commands.json."
}

$CompilerArgs = @()
$Raw = @($Entry.arguments)
for ($i = 0; $i -lt $Raw.Count; $i++) {
    $arg = [string]$Raw[$i]
    if ($i -eq 0) { continue }
    if ($arg -in @("-c", "-Zi", "-FS", "-Qunused-arguments", "-DLL_EXPORT")) { continue }
    if ($arg -like "-Fd*" -or $arg -like "-Yu*" -or $arg -like "-FI*" -or $arg -like "-Fo*") { continue }
    if ($arg -like "/Fd*" -or $arg -like "/Yu*" -or $arg -like "/FI*" -or $arg -like "/Fo*") { continue }
    if ($arg -ieq $Entry.file) { continue }
    if ($arg -ieq "-x") {
        $i++
        continue
    }
    $CompilerArgs += $arg
}

$CompilerArgs += @(
    "-imsvc", (Join-Path $Msvc.FullName "include"),
    "-imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
    "-imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
    "-imsvc", (Join-Path $SdkRoot "$($Sdk.Name)\um"),
    "-MD"
)

if ($ErrorLimit -gt 0) {
    $CompilerArgs += "-ferror-limit=$ErrorLimit"
}

if ($CompileOnly) {
    $CompilerArgs += @("/c", "/Fo:$OutputDll")
} else {
    $CompilerArgs += @("-LD", "-Fe:$OutputDll")
}

foreach ($include in $PrependIncludePath) {
    $CompilerArgs = @("-I", (Resolve-Path $include).Path) + $CompilerArgs
}

foreach ($include in $AdditionalIncludePath) {
    $CompilerArgs += @("-I", (Resolve-Path $include).Path)
}

foreach ($library in $AdditionalLinkLibrary) {
    $CompilerArgs += (Resolve-Path $library).Path
}

$LinkArgs = @()
if (-not $CompileOnly -and $DelayLoadLibrary.Count -gt 0) {
    $LinkArgs += "/link"
    foreach ($library in $DelayLoadLibrary) {
        $LinkArgs += "/DELAYLOAD:$library"
    }

    $LinkArgs += "delayimp.lib"
}

$ClangCl = Join-Path $LlvmRoot "bin\clang-cl.exe"
if (-not (Test-Path $ClangCl)) {
    throw "clang-cl was not found at $ClangCl."
}

Push-Location $LeviLaminaRoot
try {
    & $ClangCl @CompilerArgs $Source @LinkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "clang-cl exited with $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $OutputDll)) {
    throw "The BindingShim DLL was not produced at $OutputDll."
}

Write-Output "Built $OutputDll"
