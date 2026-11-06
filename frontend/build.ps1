param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$ZlibRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\deps\zlib",
    [string]$ZstdRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\deps\zstd",
    [string]$BuildDir = "build/frontend"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $LlvmRoot)) {
    throw "LLVM root not found: $LlvmRoot"
}

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
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
