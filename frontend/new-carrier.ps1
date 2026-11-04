<#
.SYNOPSIS
    Regenerates one or more binding surfaces from a new carrier checkout.

.DESCRIPTION
    Runs the mechanical part of a carrier bump end to end: resolves the carrier
    checkout, validates (or prunes) the Bedrock header list against it, builds the
    carrier with xmake when the link-carrier surface needs an import library, runs
    the frontend and emitters for every requested surface, verifies the committed
    provenance, and optionally runs the runtime smoke in a real server.

    It does not update package versions, README or profile numbers, coverage
    baselines, or the pins in frontend/carriers.json; the closing summary lists
    those follow-ups.

.PARAMETER Carrier
    levilamina or endstone.

.PARAMETER Version
    Carrier tag, for example v26.61.0. Combined with -Fetch, a checkout is added
    under Third/ as a git worktree of an existing checkout.

.PARAMETER CarrierRoot
    Existing checkout to use. Overrides -Version when both are given.

.PARAMETER Surface
    Surfaces to regenerate. Defaults to bedrock,llapi for levilamina and
    endstone for endstone.

.PARAMETER HeaderList
    Bedrock header list to parse. Defaults to the newest
    Binding.Bedrock/scripts/headers-<version>.txt, or the newest headers-*.txt
    when the carrier version has no list yet.

.PARAMETER PruneHeaderList
    Drop header paths the carrier no longer has. A path whose file name occurs
    exactly once under the carrier src tree is repointed to that file (a move);
    anything else is dropped. The result is written to -OutputHeaderList, never
    over the input list.

.PARAMETER OutputHeaderList
    Destination of the pruned list. Defaults to
    Binding.Bedrock/scripts/headers-<major>.<minor>.txt.

.PARAMETER Fetch
    Add the carrier checkout as a git worktree under Third/ when it is missing.

.PARAMETER BuildCarrier
    Force an xmake build even when LeviLamina.lib already exists. Implied when
    the llapi surface is requested and the import library is missing.

.PARAMETER UpdateJunction
    Repoint Third/LeviLamina or Third/Endstone at the carrier checkout.

.PARAMETER SkipVerify
    Skip scripts/verify_generated.py for the regenerated repositories.

.PARAMETER DryRun
    Resolve the carrier and validate the header list, then stop before generating.

.PARAMETER ServerRoot
    When set, run the repository's runtime smoke against this server directory.

.PARAMETER LlvmRoot
    LLVM 23.1.4 root used by the frontend and clang-cl.

.PARAMETER EnttRoot
    Optional entt src root forwarded to the Bedrock frontend.

.PARAMETER Xmake
    xmake executable.

.EXAMPLE
    powershell -File frontend/new-carrier.ps1 -Carrier levilamina -Version v26.61.0 -Fetch -BuildCarrier

.EXAMPLE
    powershell -File frontend/new-carrier.ps1 -Carrier levilamina -CarrierRoot ..\Third\LeviLamina-v26.61.0 -Surface bedrock

.EXAMPLE
    powershell -File frontend/new-carrier.ps1 -Carrier endstone -Version v0.11.16 -Fetch -ServerRoot ..\BDS\run-endstone
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("levilamina", "endstone")]
    [string]$Carrier,

    [string]$Version = "",
    [string]$CarrierRoot = "",

    [ValidateSet("bedrock", "llapi", "endstone")]
    [string[]]$Surface = @(),

    [string]$HeaderList = "",
    [switch]$PruneHeaderList,
    [string]$OutputHeaderList = "",

    [switch]$Fetch,
    [switch]$BuildCarrier,
    [switch]$UpdateJunction,
    [switch]$SkipVerify,
    [switch]$DryRun,

    [string]$ServerRoot = "",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.4-x86_64-pc-windows-msvc",
    [string]$EnttRoot = "",
    [string]$Xmake = "xmake"
)

$ErrorActionPreference = "Stop"

$FrontendRoot = $PSScriptRoot
$GeneratorRoot = (Resolve-Path (Join-Path $FrontendRoot "..")).Path
$Workspace = Split-Path $GeneratorRoot -Parent
$ThirdRoot = Join-Path $Workspace "Third"
$BedrockRepo = Join-Path $Workspace "Binding.Bedrock"
$LlapiRepo = Join-Path $Workspace "Binding.LeviLamina"
$EndstoneRepo = Join-Path $Workspace "Binding.Endstone"

$script:StageIndex = 0
$script:StageTotal = 0

function Start-Stage([string]$Label) {
    $script:StageIndex++
    Write-Host ""
    Write-Host "==> [$script:StageIndex/$script:StageTotal] $Label" -ForegroundColor Cyan
}

function Write-Note([string]$Text) {
    Write-Host "    $Text" -ForegroundColor DarkGray
}

function Get-ShortVersion([string]$Tag) {
    if ($Tag -match '^v?(\d+)\.(\d+)') { return "$($Matches[1]).$($Matches[2])" }
    return $null
}

function Assert-Coverage {
    param([string]$Repo, [string]$CarrierRoot, [string]$Name, [DateTime]$Since)
    $coverage = Join-Path $Repo "coverage"
    $summaryPath = Join-Path $coverage "summary.json"
    $provenancePath = Join-Path $coverage "provenance.json"
    foreach ($path in @($summaryPath, $provenancePath)) {
        if (-not (Test-Path $path)) { throw "$Name did not write $path" }
        if ((Get-Item $path).LastWriteTimeUtc -lt $Since) {
            throw "$Name did not refresh $path; the generator failed before writing coverage"
        }
    }
    if (Test-Path (Join-Path $CarrierRoot ".git")) {
        $head = (& git -C $CarrierRoot rev-parse HEAD).Trim()
        $provenance = Get-Content $provenancePath -Raw | ConvertFrom-Json
        if ($provenance.carrierCommit -and $provenance.carrierCommit -ne $head) {
            throw "$Name provenance records carrier $($provenance.carrierCommit), but the checkout is at $head"
        }
    }
}

function Get-ReposForSurface([string]$Name) {
    switch ($Name) {
        "bedrock" { return $BedrockRepo }
        "llapi" { return $LlapiRepo }
        "endstone" { return $EndstoneRepo }
    }
}

# --- Carrier and surface selection -------------------------------------------------

if ($Surface.Count -eq 0) {
    $Surface = if ($Carrier -eq "levilamina") { @("bedrock", "llapi") } else { @("endstone") }
}
$Surface = @($Surface | Select-Object -Unique)
$allowed = if ($Carrier -eq "levilamina") { @("bedrock", "llapi") } else { @("endstone") }
foreach ($name in $Surface) {
    if ($name -notin $allowed) { throw "The $Carrier carrier cannot emit the $name surface." }
}

$pinned = @{
    levilamina = (Join-Path $ThirdRoot "LeviLamina")
    endstone = (Join-Path $ThirdRoot "Endstone")
}
$prefix = if ($Carrier -eq "levilamina") { "LeviLamina" } else { "Endstone" }

# --- Resolve the carrier checkout -------------------------------------------------

if ($CarrierRoot) {
    if (-not (Test-Path $CarrierRoot)) { throw "Carrier root not found: $CarrierRoot" }
    $CarrierRoot = (Resolve-Path $CarrierRoot).Path
}
elseif ($Version) {
    $candidate = Join-Path $ThirdRoot "$prefix-$Version"
    if (-not (Test-Path $candidate)) {
        if (-not $Fetch) {
            throw "$candidate does not exist. Pass -Fetch to add it as a git worktree, or -CarrierRoot for an existing checkout."
        }
        $source = Get-ChildItem $ThirdRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "$prefix-*" -and (Test-Path (Join-Path $_.FullName ".git")) } |
            Select-Object -First 1
        if (-not $source) { throw "No $prefix checkout under $ThirdRoot to create a worktree from." }
        Write-Note "Fetching tags in $($source.FullName)"
        & git -C $source.FullName fetch --tags
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed with exit code $LASTEXITCODE." }
        Write-Note "Adding worktree $candidate at $Version"
        & git -C $source.FullName worktree add $candidate $Version
        if ($LASTEXITCODE -ne 0) { throw "git worktree add failed with exit code $LASTEXITCODE." }
    }
    $CarrierRoot = (Resolve-Path $candidate).Path
}
else {
    throw "Pass -CarrierRoot or -Version."
}
if (-not (Test-Path (Join-Path $CarrierRoot "src"))) {
    throw "$CarrierRoot does not look like a carrier checkout (no src directory)."
}
$carrierCommit = (& git -C $CarrierRoot rev-parse HEAD).Trim()

if ($UpdateJunction) {
    $link = $pinned[$Carrier]
    $item = Get-Item $link -Force -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        [System.IO.Directory]::Delete($link, $false)
    } elseif ($item) {
        throw "$link exists and is not a junction; not replacing it."
    }
    New-Item -ItemType Junction -Path $link -Target $CarrierRoot | Out-Null
}

# --- Stage outline ----------------------------------------------------------------

if (-not $DryRun -and -not (Test-Path $LlvmRoot)) {
    throw "LLVM root not found: $LlvmRoot. Pass -LlvmRoot with an extracted LLVM tree."
}
$needsCarrierBuild = [bool]$BuildCarrier
if ($Carrier -eq "levilamina" -and $Surface -contains "llapi") {
    if (-not (Test-Path (Join-Path $CarrierRoot "build\windows\x64\release\LeviLamina.lib"))) {
        $needsCarrierBuild = $true
    }
}
$frontendExe = Join-Path $GeneratorRoot "build\frontend\Release\hosihikari-ir-frontend.exe"
$needsFrontendBuild = (-not (Test-Path $frontendExe)) -and
    (($Surface -contains "llapi") -or ($Surface -contains "endstone"))

$script:StageTotal = 1
if ($Surface -contains "bedrock") { $script:StageTotal++ }
if (-not $DryRun) {
    if ($needsFrontendBuild) { $script:StageTotal++ }
    if ($needsCarrierBuild) { $script:StageTotal++ }
    $script:StageTotal += 1
    if (-not $SkipVerify) { $script:StageTotal++ }
    if ($ServerRoot) { $script:StageTotal++ }
}

Start-Stage "Resolve the $Carrier carrier"
Write-Note "Checkout: $CarrierRoot"
Write-Note "Commit:   $carrierCommit"
if ($Carrier -eq "levilamina") {
    $lua = Join-Path $CarrierRoot "xmake.lua"
    if (Test-Path $lua) {
        $match = Select-String -Path $lua -Pattern 'bedrockdata\s+(v[0-9.]+-(?:server|client)\.\d+)' | Select-Object -First 1
        if ($match) { Write-Note "Runtime data: $($match.Matches[0].Groups[1].Value)" }
    }
}
if ($UpdateJunction) { Write-Note "Repointed $($pinned[$Carrier]) -> $CarrierRoot" }

# --- Header list -------------------------------------------------------------------

if ($Surface -contains "bedrock") {
    Start-Stage "Validate the Bedrock header list"
    if (-not $HeaderList) {
        $short = Get-ShortVersion $Version
        $candidate = if ($short) { Join-Path $BedrockRepo "scripts\headers-$short.txt" } else { "" }
        if ($candidate -and (Test-Path $candidate)) {
            $HeaderList = $candidate
        } else {
            $latest = Get-ChildItem (Join-Path $BedrockRepo "scripts") -Filter "headers-*.txt" |
                Sort-Object { try { [version]($_.BaseName -replace '^headers-', '') } catch { [version]"0.0" } } |
                Select-Object -Last 1
            if (-not $latest) { throw "No headers-*.txt under $BedrockRepo\scripts." }
            $HeaderList = $latest.FullName
            if ($Version) {
                Write-Note "No list for '$Version'; validating against $($latest.Name)"
            } else {
                Write-Note "Validating against $($latest.Name)"
            }
        }
    }
    $HeaderList = (Resolve-Path $HeaderList).Path
    $carrierSrc = Join-Path $CarrierRoot "src"
    $entries = @(Get-Content $HeaderList | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $entries) {
        if (-not (Test-Path (Join-Path $carrierSrc $entry))) { $missing.Add($entry) }
    }
    Write-Note "$($entries.Count) headers, $($missing.Count) missing from $carrierSrc"
    if ($missing.Count -gt 0) {
        $missing | Select-Object -First 20 | ForEach-Object { Write-Note "  missing: $_" }
        if ($missing.Count -gt 20) { Write-Note "  ... and $($missing.Count - 20) more" }
        if (-not $PruneHeaderList) {
            throw "The header list has $($missing.Count) paths the carrier does not have. Rerun with -PruneHeaderList to write a pruned list."
        }
        if (-not $OutputHeaderList) {
            $short = Get-ShortVersion $Version
            if (-not $short) { throw "Pass -OutputHeaderList to write the pruned list." }
            $OutputHeaderList = Join-Path $BedrockRepo "scripts\headers-$short.txt"
        }
        $existingOutput = Resolve-Path $OutputHeaderList -ErrorAction SilentlyContinue
        if ($existingOutput -and $existingOutput.Path -eq $HeaderList) {
            throw "-OutputHeaderList must differ from -HeaderList; the input list is never overwritten."
        }
        $byName = @{}
        Get-ChildItem $carrierSrc -Recurse -Filter *.h | ForEach-Object {
            if (-not $byName.ContainsKey($_.Name)) { $byName[$_.Name] = New-Object System.Collections.Generic.List[string] }
            $byName[$_.Name].Add($_.FullName.Substring($carrierSrc.Length + 1).Replace('\', '/'))
        }
        $missingSet = @{}
        foreach ($entry in $missing) { $missingSet[$entry] = $true }
        $pruned = New-Object System.Collections.Generic.List[string]
        $moved = 0
        $dropped = 0
        foreach ($entry in $entries) {
            if (-not $missingSet.ContainsKey($entry)) {
                $pruned.Add($entry)
                continue
            }
            $candidates = $byName[(Split-Path $entry -Leaf)]
            if ($candidates -and $candidates.Count -eq 1) {
                $pruned.Add($candidates[0])
                $moved++
                Write-Note "  moved:   $entry -> $($candidates[0])"
            } else {
                $dropped++
                Write-Note "  dropped: $entry"
            }
        }
        $outputDirectory = Split-Path $OutputHeaderList -Parent
        if (-not (Test-Path $outputDirectory)) { New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null }
        [System.IO.File]::WriteAllLines(
            $OutputHeaderList,
            $pruned,
            (New-Object System.Text.UTF8Encoding($false)))
        Write-Note "Wrote $OutputHeaderList ($moved moved, $dropped dropped)"
        $HeaderList = $OutputHeaderList
    }
    elseif ($OutputHeaderList) {
        $existingOutput = Resolve-Path $OutputHeaderList -ErrorAction SilentlyContinue
        if ($existingOutput -and $existingOutput.Path -eq $HeaderList) {
            throw "-OutputHeaderList must differ from -HeaderList; the input list is never overwritten."
        }
        $outputDirectory = Split-Path $OutputHeaderList -Parent
        if (-not (Test-Path $outputDirectory)) { New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null }
        [System.IO.File]::WriteAllLines(
            $OutputHeaderList,
            (Get-Content $HeaderList),
            (New-Object System.Text.UTF8Encoding($false)))
        Write-Note "Wrote $OutputHeaderList ($($entries.Count) headers)"
        $HeaderList = $OutputHeaderList
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Note "Dry run: carrier and header list checked; stopping before generation."
    return
}

# --- Frontend ----------------------------------------------------------------------

if ($needsFrontendBuild) {
    Start-Stage "Build the IR frontend"
    & (Join-Path $FrontendRoot "build.ps1") -LlvmRoot $LlvmRoot
}

# --- Carrier build -----------------------------------------------------------------

if ($needsCarrierBuild) {
    Start-Stage "Build the carrier with xmake"
    $env:PATH = "$(Join-Path $LlvmRoot 'bin');$env:PATH"
    Push-Location $CarrierRoot
    try {
        & $Xmake f -y -m release
        if ($LASTEXITCODE -ne 0) { throw "xmake configure failed with exit code $LASTEXITCODE." }
        & $Xmake build -y
        if ($LASTEXITCODE -ne 0) { throw "xmake build failed with exit code $LASTEXITCODE." }
        & $Xmake project -k compile_commands build
        if ($LASTEXITCODE -ne 0) { Write-Warning "xmake project -k compile_commands failed; compile_commands.json may be stale." }
    }
    finally {
        Pop-Location
    }
    $compileCommands = Join-Path $CarrierRoot "build\compile_commands.json"
    if (Test-Path $compileCommands) {
        Copy-Item $compileCommands (Join-Path $CarrierRoot "compile_commands.json") -Force
    }
    if ($Carrier -eq "levilamina" -and -not (Test-Path (Join-Path $CarrierRoot "build\windows\x64\release\LeviLamina.lib"))) {
        throw "The carrier build did not produce build\windows\x64\release\LeviLamina.lib."
    }
}

# --- Regenerate --------------------------------------------------------------------

Start-Stage "Generate $($Surface -join ', ')"
if ($Surface -contains "bedrock") {
    $since = [DateTime]::UtcNow
    $arguments = @{
        LeviLaminaRoot = $CarrierRoot
        HeaderList = $HeaderList
        LlvmRoot = $LlvmRoot
    }
    if ($EnttRoot) { $arguments["EnttRoot"] = (Resolve-Path $EnttRoot).Path }
    & (Join-Path $BedrockRepo "scripts\regenerate.ps1") @arguments
    Assert-Coverage -Repo $BedrockRepo -CarrierRoot $CarrierRoot -Name "Bedrock" -Since $since
}

if ($Surface -contains "llapi") {
    $since = [DateTime]::UtcNow
    $ir = Join-Path $GeneratorRoot "artifacts\llapi-full-ir"
    & (Join-Path $FrontendRoot "run-ll-full.ps1") -LeviLaminaRoot $CarrierRoot -Output $ir -LlvmRoot $LlvmRoot
    & (Join-Path $LlapiRepo "scripts\regenerate.ps1") -IrDirectory $ir -LeviLaminaRoot $CarrierRoot
    Assert-Coverage -Repo $LlapiRepo -CarrierRoot $CarrierRoot -Name "LLAPI" -Since $since
}

if ($Surface -contains "endstone") {
    $since = [DateTime]::UtcNow
    $ir = Join-Path $GeneratorRoot "artifacts\endstone-ir"
    & (Join-Path $FrontendRoot "run-endstone.ps1") -EndstoneRoot $CarrierRoot -Output $ir -LlvmRoot $LlvmRoot
    & (Join-Path $EndstoneRepo "scripts\regenerate.ps1") -IrDirectory $ir -EndstoneRoot $CarrierRoot
    Assert-Coverage -Repo $EndstoneRepo -CarrierRoot $CarrierRoot -Name "Endstone" -Since $since
}

# --- Verify ------------------------------------------------------------------------

$repos = @($Surface | ForEach-Object { Get-ReposForSurface $_ } | Select-Object -Unique)
if (-not $SkipVerify) {
    Start-Stage "Verify generated sources"
    foreach ($repo in $repos) {
        Push-Location $repo
        try {
            Write-Note "$(Split-Path $repo -Leaf)"
            & python "scripts\verify_generated.py"
            if ($LASTEXITCODE -ne 0) { throw "verify_generated.py failed in $repo." }
        }
        finally {
            Pop-Location
        }
    }
}

# --- Runtime smoke -----------------------------------------------------------------

if ($ServerRoot) {
    Start-Stage "Run the runtime smoke"
    $server = (Resolve-Path $ServerRoot).Path
    if ($Surface -contains "bedrock") {
        & (Join-Path $BedrockRepo "scripts\run-runtime-smoke.ps1") -ServerRoot $server
    }
    if ($Surface -contains "llapi") {
        & (Join-Path $LlapiRepo "scripts\run-runtime-smoke.ps1") -ServerRoot $server
    }
    if ($Surface -contains "endstone") {
        & (Join-Path $EndstoneRepo "scripts\run-runtime-smoke.ps1") -ServerRoot $server -EndstoneRoot $CarrierRoot
    }
}

# --- Summary -----------------------------------------------------------------------

Write-Host ""
Write-Host "==> Summary" -ForegroundColor Cyan
foreach ($name in $Surface) {
    $repo = Get-ReposForSurface $name
    $summary = Get-Content (Join-Path $repo "coverage\summary.json") -Raw | ConvertFrom-Json
    $provenance = Get-Content (Join-Path $repo "coverage\provenance.json") -Raw | ConvertFrom-Json
    $line = "{0,-8} {1,7} declarations  {2,8} members  {3,7} functions" -f `
        $name, $summary.declarations.bound, $summary.members.bound, $provenance.functions
    Write-Host $line
    Write-Note "carrier $($provenance.carrierCommit)"
    $changes = @(& git -C $repo status --short)
    Write-Note "$($changes.Count) changed paths in $(Split-Path $repo -Leaf)"
}
Write-Host ""
Write-Note "Review the diff in each repository, then update package versions, README/profile numbers,"
Write-Note "coverage baselines, and frontend/carriers.json before committing."
