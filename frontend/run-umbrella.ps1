param(
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string]$LeviLaminaRoot = "",
    [string]$Module = "ll/api/event",
    [string]$Output = "artifacts/llapi-umbrella-ir",
    # Header-only files outside $Module that the surface has to expose (the actor
    # chain an event's self() returns, for instance). They are included in the
    # umbrella and added as filters; their directories are not pulled in whole.
    [string[]]$ExtraFilter = @(),
    # Headers included before everything else. A whole-directory $ExtraFilter is
    # sorted, so a header whose member needs a complete type further down the
    # alphabet has to be given here instead.
    [string[]]$PrependHeader = @(),
    # Subtrees a whole-directory $ExtraFilter pulls in but the surface cannot compile:
    # client-only and editor-only headers are never instantiable against the server
    # headers, so they are dropped from both the umbrella and the filters.
    [string[]]$ExcludeFilter = @()
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LeviLamina"
}

$ErrorActionPreference = "Stop"

$GeneratorRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $GeneratorRoot $Output }

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) {
    throw "Visual Studio with the C++ toolchain was not found."
}

$Msvc = Get-ChildItem (Join-Path $VsRoot "VC\Tools\MSVC") | Sort-Object Name | Select-Object -Last 1
$SdkRoot = "C:\Program Files (x86)\Windows Kits\10\Include"
$Sdk = Get-ChildItem $SdkRoot | Sort-Object Name | Select-Object -Last 1
$ClangResource = Get-ChildItem (Join-Path $LlvmRoot "lib\clang") | Sort-Object Name | Select-Object -Last 1

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
    if ($arg -in @("-c", "-MD", "-Zi", "-FS", "-O2", "-Qunused-arguments")) { continue }
    if ($arg -like "-Fd*" -or $arg -like "-Yu*" -or $arg -like "-FI*" -or $arg -like "-Fo*") { continue }
    if ($arg -like "/*") { continue }
    if ($arg -ieq $Entry.file) { continue }
    if ($arg -ieq "-x") {
        $i++
        continue
    }
    if ($arg -like "-std:*") {
        $CompilerArgs += ("-std=" + $arg.Substring(5))
        continue
    }
    $CompilerArgs += $arg
}

$CompilerArgs += @(
    "-resource-dir", (Join-Path $LlvmRoot "lib\clang\$($ClangResource.Name)"),
    "-fms-extensions",
    "-fms-compatibility",
    "-fms-compatibility-version=19.44",
    "-isystem", (Join-Path $Msvc.FullName "include"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
    "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\um")
)

$SourceRoots = @("src", "src-server")
$Headers = foreach ($root in $SourceRoots) {
    $rootPath = Join-Path $LeviLaminaRoot $root
    $headerRoot = Join-Path $rootPath $Module
    if (Test-Path $headerRoot) {
        Get-ChildItem $headerRoot -Recurse -Filter *.h | ForEach-Object {
            [pscustomobject]@{ Header = $_; Root = $rootPath }
        }
    }
}
$Headers = $Headers | Sort-Object { $_.Header.FullName }
$ExcludedPaths = @(foreach ($item in $ExcludeFilter) {
    (Resolve-Path (Join-Path $LeviLaminaRoot $item)).Path
})

function Test-Excluded([string]$Path) {
    foreach ($excluded in $ExcludedPaths) {
        if ($Path -like "$excluded*") { return $true }
    }
    return $false
}

$ExtraHeaders = foreach ($item in $ExtraFilter) {
    $full = (Resolve-Path (Join-Path $LeviLaminaRoot $item)).Path
    # Included relative to their source root (mc/...), the spelling their siblings use;
    # a src/mc prefix would let the same header load twice and split its include guard.
    $root = Join-Path $LeviLaminaRoot ($item -split '[\\/]')[0]
    if (Test-Path $full -PathType Container) {
        Get-ChildItem $full -Recurse -Filter *.h | Where-Object { -not (Test-Excluded $_.FullName) } | ForEach-Object {
            [pscustomobject]@{ Header = $_; Root = $root }
        }
    } else {
        [pscustomobject]@{ Header = Get-Item $full; Root = $root }
    }
}

$UmbrellaDir = Join-Path $GeneratorRoot "artifacts\llapi-umbrella"
New-Item -ItemType Directory -Force -Path $UmbrellaDir | Out-Null
$Umbrella = Join-Path $UmbrellaDir ("umbrella_" + ($Module -replace '/', '_') + ".h")
# The extra headers come first: they are the ones the surface has to expose, and
# parsing them before the module tree keeps an alphabetical include from landing a
# header before the type its members need (NaturalSpawner before EntityTypeCache).
$Lines = @($PrependHeader | ForEach-Object { "#include `"$_`"" })
$Lines += @(foreach ($item in @($ExtraHeaders) + @($Headers)) {
    $Relative = $item.Header.FullName.Substring($item.Root.Length + 1).Replace('\', '/')
    "#include `"$Relative`""
})
Set-Content -Path $Umbrella -Value $Lines -Encoding utf8

$FilterArgs = @()
foreach ($root in $SourceRoots) {
    $filterPath = Join-Path $LeviLaminaRoot (Join-Path $root $Module)
    if (Test-Path $filterPath) {
        $FilterArgs += @("--filter", $filterPath)
    }
}
foreach ($item in $ExtraFilter) {
    $filterPath = (Resolve-Path (Join-Path $LeviLaminaRoot $item)).Path
    if (-not (Test-Path $filterPath -PathType Container)) {
        $FilterArgs += @("--filter", $filterPath)
        continue
    }

    $boxed = $false
    foreach ($excluded in $ExcludedPaths) {
        if ($excluded -like "$filterPath*") {
            $boxed = $true
            break
        }
    }

    if (-not $boxed) {
        $FilterArgs += @("--filter", $filterPath)
        continue
    }

    # The filter is a prefix match, so one filter for the whole subtree would put the
    # excluded directories back; name each immediate child instead.
    foreach ($child in Get-ChildItem $filterPath -Directory | Sort-Object Name) {
        if (Test-Excluded $child.FullName) {
            continue
        }

        $FilterArgs += @("--filter", $child.FullName)
    }
}

$Exe = Join-Path $GeneratorRoot "build\frontend\Release\hosihikari-ir-frontend.exe"
$env:PATH = "$(Join-Path $LlvmRoot 'bin');$env:PATH"

Push-Location $LeviLaminaRoot
try {
    & $Exe `
        --surface levilamina `
        --output $Output `
        --root $LeviLaminaRoot `
        @FilterArgs `
        --configuration win-x64-server `
        --module $Module `
        --source $Umbrella `
        -- @CompilerArgs

    if ($LASTEXITCODE -ne 0) {
        throw "frontend exited with $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

dotnet run --project (Join-Path $GeneratorRoot "src\Hosihikari.Binding.Generator\Hosihikari.Binding.Generator.csproj") -c Release -- validate-ir "$Output/ir-manifest.json" "$Output"
