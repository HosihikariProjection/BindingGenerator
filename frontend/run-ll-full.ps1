param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$LeviLaminaRoot = "",
    [string]$Output = "artifacts/llapi-full-ir"
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$GeneratorRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LlvmRoot = (Resolve-Path $LlvmRoot).Path
$LeviLaminaRoot = (Resolve-Path $LeviLaminaRoot).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $GeneratorRoot $Output }

# One umbrella over src/ll/api plus src-server/ll/api. The visitor records
# dependency enums declared outside the filter, so no separate merge pass is
# needed when the full API tree is parsed at once.
& (Join-Path $PSScriptRoot "run-umbrella.ps1") `
    -LlvmRoot $LlvmRoot `
    -LeviLaminaRoot $LeviLaminaRoot `
    -Module "ll/api" `
    -Output $Output
