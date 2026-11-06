param(
    [string]$LeviLaminaRoot = "",
    [string]$EnttRoot = "C:\Users\root\AppData\Local\Temp\entt-41aab92\src",
    [string]$Output = "artifacts/levi-1.21.3-header-ir",
    [string]$LlvmRoot = "$env:LOCALAPPDATA\Temp\hosihikari-llvm\clang+llvm-23.1.0-x86_64-pc-windows-msvc",
    [string[]]$AdditionalIncludePath = @(),
    [string[]]$ExternalRoot = @(),
    [string]$HeaderList = "",
    [string]$AppendSource = "",
    [switch]$UseCompileCommands,
    [switch]$EmitMangledNames,
    [switch]$EmitImplicitDestructors,
    [switch]$EmitVTableIndices,
    [switch]$SkipBuild
)
if ([string]::IsNullOrWhiteSpace($LeviLaminaRoot)) {
    $LeviLaminaRoot = Join-Path $PSScriptRoot "..\..\Third\LiteLoaderBDS"
}

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LeviLaminaRoot = (Resolve-Path $LeviLaminaRoot).Path
$EnttRoot = (Resolve-Path $EnttRoot).Path
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $RepoRoot $Output }
$SourceRoot = Join-Path $LeviLaminaRoot "src"

$Headers = @(
    "mc/world/actor/Actor.h",
    "mc/world/actor/Mob.h",
    "mc/world/actor/player/Player.h",
    "mc/world/level/dimension/Dimension.h",
    "mc/world/level/block/Block.h",
    "mc/world/item/Item.h",
    "mc/world/Container.h",
    "mc/math/Vec3.h",
    "mc/world/level/BlockPos.h",
    "mc/world/level/ChunkPos.h",
    "mc/world/level/BlockSource.h",
    "mc/world/level/Level.h",
    "mc/world/item/registry/ItemStack.h",
    "mc/world/item/registry/ItemRegistryRef.h",
    "mc/world/phys/AABB.h",
    "mc/network/packet/ActorLink.h",
    "mc/world/ActorUniqueID.h",
    "mc/world/ActorRuntimeID.h",
    "mc/world/effect/MobEffectInstance.h",
    "mc/world/actor/DistanceSortedActor.h",
    "mc/world/item/ItemDescriptor.h",
    "mc/math/Vec2.h",
    "mc/common/Brightness.h",
    "mc/common/BrightnessPair.h",
    "mc/deps/core/data/InterpolationPair.h",
    "mc/world/level/LevelSeed64.h",
    "mc/deps/core/mce/Color.h",
    "mc/enums/PlatformType.h",
    "mc/enums/ARVRPlatform.h",
    "mc/enums/AppFocusState.h",
    "mc/enums/OsVersion.h",
    "mc/enums/DeviceSunsetTier.h",
    "mc/enums/UIScalingRules.h",
    "mc/enums/VRControllerType.h",
    "mc/network/NetworkConnectionType.h",
    "mc/deps/core/secure_storage/SecureStorageKey.h",
    "mc/nbt/CompoundTag.h",
    "mc/nbt/ListTag.h",
    "mc/deps/core/secure_storage/SecureStorage.h",
    "mc/world/level/LevelStorageManager.h",
    "mc/deps/core/common/bedrock/PlatformRuntimeInfo.h",
    "mc/world/level/pathfinder/Path.h",
    "mc/world/events/EventResult.h",
    "mc/world/item/ItemInstance.h",
    "mc/common/wrapper/WeakPtr.h",
    "mc/common/wrapper/InteractionResult.h",
    "mc/world/inventory/simulation/ContainerValidationResult.h",
    "mc/world/item/ResolvedItemIconInfo.h",
    "mc/deps/core/sem_ver/SemVersion.h",
    "mc/network/packet/UpdateTradePacket.h",
    "mc/world/actor/ActorDefinitionIdentifier.h",
    "mc/world/level/ChunkLocalHeight.h",
    "mc/enums/TransportLayer.h",
    "mc/world/level/LevelSettings.h",
    "mc/world/level/block/BlockDescriptor.h",
    "mc/util/ExpressionNode.h",
    "mc/world/item/components/ItemContext.h",
    "mc/world/level/storage/LevelData.h",
    "mc/world/item/crafting/MerchantRecipe.h",
    "mc/world/level/biome/MobSpawnRules.h",
    "mc/entity/components/ProjectileComponent.h",
    "mc/math/Random.h",
    "mc/server/commands/VirtualCommandOrigin.h",
    "mc/world/item/enchanting/Enchant.h",
    "mc/enums/SignTextSide.h",
    "mc/world/level/chunk/SubChunkLightIndex.h",
    "mc/enums/connection/DisconnectFailReason.h",
    "mc/world/item/alchemy/Potion.h",
    "mc/common/DBHelpers.h",
    "mc/world/level/storage/GameRuleId.h",
    "mc/enums/edu/Role.h",
    "mc/events/IMinecraftEventing.h",
    "mc/resources/PackType.h",
    "mc/scripting/modules/minecraft/ScriptModuleMinecraft.h",
    "mc/enums/DwellerRole.h",
    "mc/client/network/realms/RealmsPurchaseIntent.h",
    "mc/server/sim/LookDuration.h",
    "mc/enums/FullscreenMode.h",
    "mc/world/level/chunk/ChunkSource.h",
    "mc/world/Facing.h",
    "mc/world/actor/ActorDamageSource.h",
    "mc/enums/PurchasePath.h",
    "mc/enums/HardcodedSpawnAreaType.h",
    "mc/client/network/realms/RealmId.h",
    "mc/events/MinecraftEventing.h"
)
# The 1.21.3 supplement list lives in headers/mc-1.21.3.txt so layout headers can grow
# without editing this script.
if ([string]::IsNullOrWhiteSpace($HeaderList)) {
    $HeaderList = Join-Path $PSScriptRoot "headers/mc-1.21.3.txt"
}
if (-not [string]::IsNullOrWhiteSpace($HeaderList)) {
    $HeaderListPath = (Resolve-Path $HeaderList).Path
    $Headers = Get-Content $HeaderListPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" -and -not $_.StartsWith("#") }
}
foreach ($header in $Headers) {
    if (-not (Test-Path (Join-Path $SourceRoot $header))) {
        throw "LeviLamina header was not found: $header"
    }
}
if (-not (Test-Path (Join-Path $EnttRoot "entt/entt.hpp"))) {
    throw "pinned entt headers were not found under $EnttRoot."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -LlvmRoot $LlvmRoot
}

$Frontend = Join-Path $RepoRoot "build\frontend\Release\hosihikari-ir-frontend.exe"
if (-not (Test-Path $Frontend)) {
    throw "IR frontend was not built: $Frontend"
}

$Umbrella = Join-Path $Output "umbrella.hpp"
New-Item -ItemType Directory -Force -Path $Output | Out-Null
# CommandOutput.h uses fmt::format_string without including fmt itself; the game's build
# supplies it through its precompiled header, so provide it from the umbrella instead.
$UmbrellaPrelude = @("#include <fmt/format.h>", "#include <fmt/args.h>")
$UmbrellaText = ($UmbrellaPrelude + ($Headers | ForEach-Object { "#include `"$_`"" })) -join "`n"
if (-not [string]::IsNullOrWhiteSpace($AppendSource)) {
    $UmbrellaText += "`n" + [System.IO.File]::ReadAllText((Resolve-Path $AppendSource).Path)
}
[System.IO.File]::WriteAllText($Umbrella, "$UmbrellaText`n", (New-Object System.Text.UTF8Encoding($false)))

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsRoot = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsRoot) { throw "Visual Studio with the C++ toolchain was not found." }

$Msvc = Get-ChildItem (Join-Path $VsRoot "VC\Tools\MSVC") | Sort-Object Name | Select-Object -Last 1
$SdkRoot = "C:\Program Files (x86)\Windows Kits\10\Include"
$Sdk = Get-ChildItem $SdkRoot | Sort-Object Name | Select-Object -Last 1

$Includes = @("-I", $EnttRoot, "-I", $SourceRoot, "-I", (Join-Path $SourceRoot "mc"))
foreach ($package in Get-ChildItem "$env:LOCALAPPDATA\.xmake\packages" -Recurse -Directory -Filter "include" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\e\\entt\\' } | Sort-Object FullName -Unique) {
    $Includes += @("-I", $package.FullName)
}
foreach ($include in $AdditionalIncludePath) {
    $Includes += @("-I", (Resolve-Path $include).Path)
}

if ($UseCompileCommands) {
    $CompileCommandsPath = Join-Path $LeviLaminaRoot "compile_commands.json"
    if (-not (Test-Path $CompileCommandsPath)) {
        throw "compile_commands.json was not found under $LeviLaminaRoot."
    }
    $CompileCommands = Get-Content $CompileCommandsPath -Raw | ConvertFrom-Json
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

    $CompilerArgs = @("-I", (Join-Path $LeviLaminaRoot "src-server")) + $CompilerArgs
    $CompilerArgs += @(
        "-fms-extensions",
        "-fms-compatibility",
        "-fms-compatibility-version=19.44",
        "-isystem", (Join-Path $Msvc.FullName "include"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\um")
    )
}
else {
    $CompilerArgs = @(
        "-std=c++23",
        "-Dnsel_CONFIG_SELECT_EXPECTED=nsel_EXPECTED_NONSTD",
        "-fms-extensions",
        "-fms-compatibility",
        "-fms-compatibility-version=19.44",
        "-D_WIN64",
        "-D_AMD64_",
        "-DNOMINMAX",
        "-DWIN32_LEAN_AND_MEAN"
    ) + $Includes + @(
        "-isystem", (Join-Path $Msvc.FullName "include"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\ucrt"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\shared"),
        "-isystem", (Join-Path $SdkRoot "$($Sdk.Name)\um")
    )
}

# A prelude may instantiate header templates with arguments the tree only forward-declares;
# keep parsing past clang's default error limit so the remaining declarations survive.
$CompilerArgs += "-ferror-limit=0"

$FrontendArgs = @(
    "--surface", "bedrock",
    "--output", $Output,
    "--root", $SourceRoot,
    "--filter", (Join-Path $SourceRoot "mc"),
    "--configuration", "win-x64-server",
    "--module", "mc",
    "--source", $Umbrella,
    "--flatten-anonymous-fields"
)
if ($EmitMangledNames) {
    $FrontendArgs += "--emit-mangled-names"
}
if ($EmitImplicitDestructors) {
    $FrontendArgs += "--emit-implicit-destructors"
}
if ($EmitVTableIndices) {
    $FrontendArgs += "--emit-vtable-indices"
}
foreach ($Root in $ExternalRoot) {
    $FrontendArgs += @("--external-root", (Resolve-Path $Root).Path)
}
& $Frontend @FrontendArgs -- @CompilerArgs

if ($LASTEXITCODE -ne 0) {
    throw "IR frontend exited with $LASTEXITCODE"
}

dotnet run --project (Join-Path $RepoRoot "src\Hosihikari.Bindings.Generator\Hosihikari.Bindings.Generator.csproj") -c Release -- validate-ir "$Output/ir-manifest.json" "$Output"
