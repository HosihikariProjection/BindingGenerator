param(
    [Parameter(Mandatory = $true)]
    [string]$ShimDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDll,
    [string]$LeviLaminaRoot = "",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$CarrierImportLibrary = "",
    [string]$FmtLib = "",
    [string]$BedrockRuntimeData = "",
    [string]$PrelinkExe = "",
    [string]$Target = "server-windows-x64",
    [string[]]$AdditionalIncludePath = @(),
    [string[]]$AdditionalLinkLibrary = @(),
    [switch]$DelayLoad
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$ShimDirectory = (Resolve-Path $ShimDirectory).Path
$OutputDll = [System.IO.Path]::GetFullPath($OutputDll)
$LeviLaminaRoot = (Resolve-Path $LeviLaminaRoot).Path
$OutputDirectory = Split-Path $OutputDll -Parent
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$Object = [System.IO.Path]::ChangeExtension($OutputDll, ".obj")

if ([string]::IsNullOrWhiteSpace($CarrierImportLibrary)) {
    $CarrierImportLibrary = Join-Path $LeviLaminaRoot "build\windows\x64\release\LeviLamina.lib"
}
$CarrierImportLibrary = (Resolve-Path $CarrierImportLibrary).Path

if ([string]::IsNullOrWhiteSpace($FmtLib)) {
    $Fmt = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\f\fmt" -Recurse -Filter fmt.lib -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $Fmt) {
        throw "fmt.lib was not found; pass -FmtLib explicitly."
    }

    $FmtLib = $Fmt.FullName
} else {
    $FmtLib = (Resolve-Path $FmtLib).Path
}

if ([string]::IsNullOrWhiteSpace($BedrockRuntimeData)) {
    $DataCandidates = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\b\bedrockdata" -Recurse -Filter bedrock_runtime_data -ErrorAction SilentlyContinue
    # The carrier pins one bedrockdata revision; the highest build number is not
    # necessarily the one it targets (26.60 pins server.5 while server.12 is 26.51).
    $Pinned = $null
    $CarrierLua = Join-Path $LeviLaminaRoot "xmake.lua"
    if (Test-Path $CarrierLua) {
        $Match = Select-String -Path $CarrierLua -Pattern 'bedrockdata\s+(v[0-9.]+-server\.\d+)' | Select-Object -First 1
        if ($Match) {
            $PinnedVersion = $Match.Matches[0].Groups[1].Value
            $Pinned = $DataCandidates | Where-Object { $_.FullName -match [regex]::Escape("\$PinnedVersion\") } | Select-Object -First 1
        }
    }
    $Data = $Pinned
    if (-not $Data) {
        $Data = $DataCandidates |
            Where-Object { $_.FullName -match '-server\.(\d+)' } |
            Sort-Object { [int]($_.FullName -replace '.*-server\.(\d+).*', '$1') } -Descending |
            Select-Object -First 1
    }
    if (-not $Data) {
        $Data = $DataCandidates | Select-Object -First 1
    }
    if (-not $Data) {
        throw "bedrock_runtime_data was not found; pass -BedrockRuntimeData explicitly."
    }

    $BedrockRuntimeData = $Data.FullName
} else {
    $BedrockRuntimeData = (Resolve-Path $BedrockRuntimeData).Path
}

if ([string]::IsNullOrWhiteSpace($PrelinkExe)) {
    $Prelink = Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages\p\prelink" -Recurse -Filter prelink.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $Prelink) {
        throw "prelink.exe was not found; pass -PrelinkExe explicitly."
    }

    $PrelinkExe = $Prelink.FullName
} else {
    $PrelinkExe = (Resolve-Path $PrelinkExe).Path
}

& (Join-Path $PSScriptRoot "build-shim.ps1") `
    -ShimDirectory $ShimDirectory `
    -OutputDll $Object `
    -LlvmRoot $LlvmRoot `
    -LeviLaminaRoot $LeviLaminaRoot `
    -AdditionalIncludePath $AdditionalIncludePath `
    -CompileOnly

$PrelinkDirectory = Join-Path $OutputDirectory "prelink"
if (Test-Path $PrelinkDirectory) {
    Remove-Item $PrelinkDirectory -Recurse -Force
}
$PrelinkLibDirectory = Join-Path $PrelinkDirectory "lib"
New-Item -ItemType Directory -Force -Path $PrelinkLibDirectory | Out-Null

$PrelinkInputs = @($Object, $CarrierImportLibrary, $FmtLib) + $AdditionalLinkLibrary
& $PrelinkExe $Target $PrelinkDirectory $BedrockRuntimeData @PrelinkInputs
if ($LASTEXITCODE -ne 0) {
    throw "prelink exited with $LASTEXITCODE"
}

$BedrockRuntimeLibrary = Join-Path $PrelinkLibDirectory "bedrock_runtime_api.lib"
if (-not (Test-Path $BedrockRuntimeLibrary)) {
    throw "prelink did not produce $BedrockRuntimeLibrary."
}

$ClangCl = Join-Path $LlvmRoot "bin\clang-cl.exe"
$LinkArgs = @("/link", "/DELAYLOAD:bedrock_runtime.dll", "delayimp.lib")
if ($DelayLoad) {
    $LinkArgs += "/DELAYLOAD:LeviLamina.dll"
}

$Libraries = @($CarrierImportLibrary, $FmtLib, $BedrockRuntimeLibrary) + $AdditionalLinkLibrary
& $ClangCl /LD /MD $Object @Libraries "/Fe:$OutputDll" @LinkArgs
if ($LASTEXITCODE -ne 0) {
    throw "clang-cl exited with $LASTEXITCODE"
}

if (-not (Test-Path $OutputDll)) {
    throw "The carrier BindingShim DLL was not produced at $OutputDll."
}

Remove-Item $Object -Force -ErrorAction SilentlyContinue
Write-Output "Built $OutputDll"
