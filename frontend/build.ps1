param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$ZlibRoot = "",
    [string]$ZstdRoot = "",
    [string]$BuildDir = "build/frontend"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $LlvmRoot)) {
    throw "LLVM root not found: $LlvmRoot"
}

# LLVMSupport pulls zlib and zstd. Take the xmake package that carries each library when the
# caller does not name one, so a cleaned package store does not break the link.
function Resolve-CompressionRoot([string]$Vendor, [string]$Name, [string[]]$Libraries) {
    $PackageRoot = Join-Path $env:LOCALAPPDATA (Join-Path ".xmake\packages" (Join-Path $Vendor $Name))
    $Roots = @(Get-ChildItem $PackageRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            $Hit = Get-ChildItem $_.FullName -Recurse -Directory -Filter "lib" -ErrorAction SilentlyContinue |
                Where-Object {
                    $dir = $_
                    @($Libraries | Where-Object { Test-Path (Join-Path $dir.FullName $_) }).Count -gt 0
                } | Select-Object -First 1
            if ($Hit) {
                $Segments = [regex]::Matches($_.FullName, '\d+') | ForEach-Object { ([int]$_.Value).ToString('D6') }
                [pscustomobject]@{ Path = (Split-Path $Hit.FullName -Parent); Version = ($Segments -join '.') }
            }
        } |
        Sort-Object { $_.Version })
    if ($Roots.Count -eq 0) { return "" }
    return $Roots[-1].Path
}

if ([string]::IsNullOrWhiteSpace($ZlibRoot)) {
    $ZlibRoot = Resolve-CompressionRoot "z" "zlib" @("zlibstatic.lib", "zlib.lib")
}
if ([string]::IsNullOrWhiteSpace($ZstdRoot)) {
    $ZstdRoot = Resolve-CompressionRoot "z" "zstd" @("zstd_static.lib", "zstd.lib")
}

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) {
    # The component id is not registered for the preview channel.
    $VsRoot = & $VsWhere -all -prerelease -products * -property installationPath |
        Where-Object { Test-Path (Join-Path $_ "VC\Tools\MSVC") } |
        Select-Object -First 1
}
if (-not $VsRoot) {
    throw "Visual Studio with the C++ toolchain was not found."
}

$CmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$Cmake = if ($CmakeCommand) {
    $CmakeCommand.Source
} else {
    Join-Path $VsRoot "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}

Push-Location (Join-Path $PSScriptRoot "..")
try {
    & $Cmake -S frontend -B $BuildDir -G "Visual Studio 18 2026" -A x64 `
        -DCMAKE_PREFIX_PATH="$LlvmRoot" `
        -DHOSIHIKARI_ZLIB_ROOT="$ZlibRoot" `
        -DHOSIHIKARI_ZSTD_ROOT="$ZstdRoot"
    if ($LASTEXITCODE -ne 0) {
        throw "cmake configure exited with $LASTEXITCODE"
    }

    & $Cmake --build $BuildDir --config Release
    if ($LASTEXITCODE -ne 0) {
        throw "cmake build exited with $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
