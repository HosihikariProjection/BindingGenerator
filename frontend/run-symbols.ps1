param(
    [Parameter(Mandatory = $true)]
    [string]$Pdb,
    [Parameter(Mandatory = $true)]
    [string]$Exe,
    [string]$Elf = "",
    [string]$Output = "artifacts/symbol-ir/prototype.json",
    [string]$IrOutput = "",
    [switch]$NoPrototype,
    [string]$Version = "1.21.3",
    [string[]]$Classes = @("Actor", "BlockPos"),
    [string]$ClassesFile = "",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Pdb = (Resolve-Path $Pdb).Path
$Exe = (Resolve-Path $Exe).Path
if (-not [string]::IsNullOrWhiteSpace($Elf)) {
    $Elf = (Resolve-Path $Elf).Path
}
$Output = if ($NoPrototype) {
    ""
} elseif ([string]::IsNullOrWhiteSpace($Output)) {
    ""
} elseif ([System.IO.Path]::IsPathRooted($Output)) {
    $Output
} else {
    Join-Path $RepoRoot $Output
}
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Output -Parent) | Out-Null
}
if (-not [string]::IsNullOrWhiteSpace($IrOutput)) {
    $IrOutput = if ([System.IO.Path]::IsPathRooted($IrOutput)) { $IrOutput } else { Join-Path $RepoRoot $IrOutput }
    New-Item -ItemType Directory -Force -Path $IrOutput | Out-Null
}
if ([string]::IsNullOrWhiteSpace($Output) -and [string]::IsNullOrWhiteSpace($IrOutput)) {
    throw "Specify -Output, -IrOutput, or both."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -LlvmRoot $LlvmRoot
}

$Frontend = Join-Path $RepoRoot "build\frontend\Release\hosihikari-symbol-ir-frontend.exe"
if (-not (Test-Path $Frontend)) {
    throw "Symbol frontend was not built: $Frontend"
}

$Arguments = @(
    "--pdb", $Pdb,
    "--exe", $Exe,
    "--version", $Version
)
if (-not [string]::IsNullOrWhiteSpace($ClassesFile)) {
    $Arguments += @("--classes-file", ((Resolve-Path $ClassesFile).Path))
} else {
    $Arguments += @("--classes", ($Classes -join ","))
}
if (-not [string]::IsNullOrWhiteSpace($Elf)) {
    $Arguments += @("--elf", $Elf)
}
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $Arguments += @("--output", $Output)
}
if (-not [string]::IsNullOrWhiteSpace($IrOutput)) {
    $Arguments += @("--ir-output", $IrOutput)
}
if ($VerbosePreference -ne 'SilentlyContinue') {
    $Arguments += @("--verbose")
}

& $Frontend @Arguments

if ($LASTEXITCODE -ne 0) {
    throw "symbol frontend exited with $LASTEXITCODE"
}

if (-not [string]::IsNullOrWhiteSpace($Output)) {
    Write-Output "Wrote $Output"
}
if (-not [string]::IsNullOrWhiteSpace($IrOutput)) {
    Write-Output "Wrote IR under $IrOutput"
}
