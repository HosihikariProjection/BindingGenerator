using System.Runtime.InteropServices;
using Hosihikari.Binding.Bedrock.Interop;

namespace Hosihikari.SymbolSmoke;

/// <summary>Entry point resolved through <c>hostfxr</c> by the preload-native plugin.</summary>
public static class NativeEntryPoint
{
    [UnmanagedCallersOnly]
    public static int Run(IntPtr argument, int argumentLength)
    {
        var resultPath = Environment.GetEnvironmentVariable("HS_SYMBOL_RESULT");
        try
        {
            var shim =
                Environment.GetEnvironmentVariable("HS_SYMBOL_SHIM")
                ?? throw new InvalidOperationException("HS_SYMBOL_SHIM is not set.");
            NativeMethods.Load(shim);

            using var a = BlockPos.Create(1.0f, 2.0f, 3.0f);
            using var b = BlockPos.Create(1.0f, 2.0f, 3.0f);
            using var c = BlockPos.Create(1.0f, 2.0f, 4.0f);

            // The hash suffix on toString depends on the IR; resolve it by shape instead.
            var toString = typeof(BlockPos)
                .GetMethods()
                .First(method =>
                    method.Name.StartsWith("toString", StringComparison.Ordinal)
                    && method.GetParameters().Length == 0
                    && method.ReturnType == typeof(string)
                );
            string Text(BlockPos value) => (string)toString.Invoke(value, null)!;

            var textA = Text(a);
            var textB = Text(b);
            var textC = Text(c);
            var seedA = a.randomSeed();
            var seedB = b.randomSeed();

            var fields = a.x == 1 && a.y == 2 && a.z == 3;
            a.x = 7;
            var textA2 = Text(a);

            using var moved = a.neighbor(0);
            var movedText = Text(moved);
            using var rebuilt = BlockPos.Create((float)moved.x, (float)moved.y, (float)moved.z);
            var rebuiltText = Text(rebuilt);

            using var origin = BlockPos.Create(17.0f, 34.0f, 51.0f);
            using var chunk = ChunkPos.Create(origin);
            using var vector = chunk.toBlockSpaceVec3(64.0f);
            var vectorLength = vector.length();
            var expectedLength = MathF.Sqrt(
                vector.x * vector.x + vector.y * vector.y + vector.z * vector.z
            );
            using var normalized = vector.normalized();
            var normalizedLength = normalized.length();
            using var absolute = vector.abs();
            var absoluteLength = absolute.length();

            // The native host reads ServiceLocator<AppPlatform>::mService, a live server
            // object with a real vtable. The getters below exercise scalar, container, and
            // string results against the running server.
            var platform = argument != IntPtr.Zero ? AppPlatform.Borrow(argument) : null;
            var eduMode = platform is not null && platform.isEduMode();
            var maxRequests = platform?.getMaxSimultaneousServiceRequests() ?? 0;
            var screenX = platform?.getDefaultScreenPositionX() ?? float.NaN;
            var eduModeAgain = platform is not null && platform.isEduMode();
            var maxRequestsAgain = platform?.getMaxSimultaneousServiceRequests() ?? uint.MaxValue;

            // These virtual getters return std::vector<std::string> through the hidden sret
            // buffer. The shim copies the elements into a thread-local view that the managed
            // side decodes immediately. A header stub can share the signature with the symbol
            // method, so resolve the bound overload by shape.
            string[] Addresses() =>
                (string[])Bound(
                    platform!,
                    nameof(AppPlatform),
                    "getIPAddresses",
                    "System.String[]"
                );
            string[] Broadcast() =>
                (string[])Bound(
                    platform!,
                    nameof(AppPlatform),
                    "getBroadcastAddresses",
                    "System.String[]"
                );
            string[] UserInput() =>
                (string[])Bound(platform!, nameof(AppPlatform), "getUserInput", "System.String[]");
            var addresses = platform is null ? [] : Addresses();
            var addressesAgain = platform is null ? [] : Addresses();
            var broadcast = platform is null ? [] : Broadcast();
            var userInput = platform is null ? [] : UserInput();
            var addressSample = addresses.Length > 0 ? addresses[0] : "";

            // ServerInstance exposes NonOwnerPointer results. The shim keeps the returned
            // control block alive and surfaces the object stored in it as the handle target.
            var serverInstanceText = Environment.GetEnvironmentVariable(
                "HS_SYMBOL_SERVER_INSTANCE"
            );
            var serverInstance =
                ulong.TryParse(serverInstanceText, out var serverAddress) && serverAddress != 0
                    ? ServerInstance.Borrow((nint)serverAddress)
                    : null;
            using var cdnConfig = serverInstance?.getCDNConfig();
            using var editorManager = serverInstance?.getEditorManager();
            using var serverTextSettings = serverInstance?.getServerTextSettings();
            var serverWrappersOk =
                serverInstance is null
                || (serverTextSettings is not null && serverTextSettings.Target != 0);

            // getSecureStorage transfers a unique_ptr<SecureStorage>. SecureStorage is a pure
            // interface, so the header supplement proves no layout for it and the projection
            // does not hand out ownership of an object it cannot size.
            var secureStorage = platform?.getSecureStorage();
            var secureStorageOk = secureStorage is null || secureStorage.Handle != 0;

            // Static constants are read from the game module's data symbols.
            using var zeroVector = Vec3.ZERO;
            using var maxBlockPos = BlockPos.MAX;
            using var emptyItem = ItemStack.EMPTY_ITEM;
            var staticConstantsOk =
                zeroVector.x == 0
                && zeroVector.y == 0
                && zeroVector.z == 0
                && maxBlockPos.x != 0
                && Level.IS_SLEEPING_POSSIBLE.Length > 0
                && Actor.DAMAGE_NEARBY_MOBS_DURATION >= 0
                && emptyItem.Handle != 0;

            // The Item class exposes static string constants; the empty stack exercises the
            // ItemStackBase.getItem pointer-return path without constructing a new stack.
            var itemStaticsOk =
                Item.TAG_DAMAGE.Length > 0 && Item.ICON_DESCRIPTION_PREFIX.Length > 0;
            using var emptyStackView = ItemStackBase.Borrow(emptyItem.Handle);
            var emptyItemPtr = emptyStackView.getItem();
            var itemLiveOk = itemStaticsOk && (emptyItemPtr is null || emptyItemPtr.Handle != 0);

            // A static member that returns std::string by value uses the same hidden return
            // buffer as a free function. Resolve the hash suffix by shape.
            var hyphenate = typeof(NetworkIdentifier)
                .GetMethods()
                .First(method =>
                    method.Name.StartsWith("hyphenateId", StringComparison.Ordinal)
                    && method.IsStatic
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(ulong)
                    && method.ReturnType == typeof(string)
                );
            var hyphenated = (string)hyphenate.Invoke(null, new object[] { 0x1234567890ABCDEFUL })!;
            var staticStringOk = hyphenated.Length > 0;

            // Core::PathBuffer<std::string> is a single string member; the shim reads it with
            // the std::string return ABI and releases it with the game's own string destructor.
            var storagePath = platform is null
                ? ""
                : (string)Bound(
                    platform,
                    nameof(AppPlatform),
                    "getInternalStoragePath",
                    "System.String"
                );
            var userdataPath = platform is null
                ? ""
                : (string)Bound(platform, nameof(AppPlatform), "getUserdataPath", "System.String");
            var currentStoragePath = platform is null
                ? ""
                : (string)Bound(
                    platform,
                    nameof(AppPlatform),
                    "getCurrentStoragePath",
                    "System.String"
                );

            // A static field whose type is not projected still resolves through its data
            // symbol and surfaces the storage address. NORMAL is Facing's unit vector table.
            var facingAddress = Facing.NORMAL_Address;
            var facingOk = facingAddress != 0;

            // Value-type round trips over the register and sret paths, plus field access.
            using var unitX = Vec3.UNIT_X;
            using var zero = Vec3.ZERO;
            var unitLength = unitX.length();
            var zeroLength = zero.length();
            using var sum = BlockPos.Create(1.0f, 2.0f, 3.0f);
            var summed = sum.x + sum.y + sum.z;
            var valueTypesOk =
                MathF.Abs(unitLength - 1.0f) < 0.01f && zeroLength == 0.0f && summed == 6.0f;

            // std::string by-value returns from both a member and a static member.
            var semVersionOk = false;
            try
            {
                using var version = SemVersion.Create(1, 2, 3, "", "");
                var rendered = version.asString_d1ecd681();
                semVersionOk = rendered.Contains("1.2.3", StringComparison.Ordinal);
            }
            catch (Exception)
            {
                semVersionOk = false;
            }

            // A static string hashing helper exercises the scalar-in, scalar-out path.
            var hashOk =
                HashedString.computeHash("starlight") is var hashA
                && hashA == HashedString.computeHash("starlight")
                && hashA != HashedString.computeHash("starlight2");

            var ok =
                textA.Length > 0
                && textA == textB
                && textA != textC
                && seedA == seedB
                && fields
                && textA2 != textA
                && movedText == rebuiltText
                && chunk.x == 1
                && vectorLength > 0.0f
                && MathF.Abs(vectorLength - expectedLength) < 0.01f
                && MathF.Abs(normalizedLength - 1.0f) < 0.01f
                && MathF.Abs(absoluteLength - vectorLength) < 0.01f
                && platform is not null
                && eduMode == eduModeAgain
                && maxRequests == maxRequestsAgain
                && float.IsFinite(screenX)
                && addresses.SequenceEqual(addressesAgain)
                && broadcast is not null
                && userInput is not null
                && storagePath.Length > 0
                && userdataPath.Length > 0
                && currentStoragePath.Length > 0
                && serverWrappersOk
                && secureStorageOk
                && staticConstantsOk
                && itemLiveOk
                && staticStringOk
                && facingOk
                && valueTypesOk
                && semVersionOk
                && hashOk;
            var result =
                "load=managed\n"
                + $"a={textA}\n"
                + $"b={textB}\n"
                + $"c={textC}\n"
                + $"a2={textA2}\n"
                + $"moved={movedText}\n"
                + $"rebuilt={rebuiltText}\n"
                + $"seedA={seedA}\n"
                + $"seedB={seedB}\n"
                + $"chunk=({chunk.x},{chunk.y},{chunk.z})\n"
                + $"vec=({vector.x},{vector.y},{vector.z})\n"
                + $"length={vectorLength}\n"
                + $"normalized={normalizedLength}\n"
                + $"absolute={absoluteLength}\n"
                + $"platform=0x{argument:X}\n"
                + $"edu={eduMode}\n"
                + $"maxRequests={maxRequests}\n"
                + $"screenX={screenX}\n"
                + $"addresses={addresses.Length}\n"
                + $"address={addressSample}\n"
                + $"broadcast={broadcast.Length}\n"
                + $"userInput={userInput.Length}\n"
                + $"storagePath={storagePath}\n"
                + $"userdataPath={userdataPath}\n"
                + $"currentStoragePath={currentStoragePath}\n"
                + $"serverInstance=0x{(serverInstance?.Handle ?? 0):X}\n"
                + $"cdnConfig={cdnConfig is not null}\n"
                + $"editorManager={editorManager is not null}\n"
                + $"serverTextSettings={(serverTextSettings is not null ? "0x" + serverTextSettings.Target.ToString("x") : "null")}\n"
                + $"secureStorage={(secureStorage is not null)}\n"
                + $"staticConstants={staticConstantsOk}\n"
                + $"itemLive={itemLiveOk}\n"
                + $"staticString={staticStringOk}\n"
                + $"facing={facingOk}\n"
                + $"valueTypes={valueTypesOk}\n"
                + $"semVersion={semVersionOk}\n"
                + $"hash={hashOk}\n"
                + $"stable={eduMode == eduModeAgain && maxRequests == maxRequestsAgain && addresses.SequenceEqual(addressesAgain)}\n"
                + $"result={(ok ? "OK" : "FAIL")}\n";
            if (resultPath is not null)
            {
                File.WriteAllText(resultPath, result);
            }

            return ok ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (resultPath is not null)
            {
                try
                {
                    File.WriteAllText(
                        resultPath,
                        "load=failed\nresult=FAIL\n" + exception + Environment.NewLine
                    );
                }
                catch (IOException) { }
            }

            return 1;
        }
    }

    /// <summary>
    /// Finds the bound overload of a zero-argument method by its declared return type. Name
    /// suffixes shift whenever the IR gains a stub with the same signature.
    /// </summary>
    private static object Bound(
        object target,
        string typeName,
        string methodName,
        string returnType
    )
    {
        var match = target
            .GetType()
            .GetMethods()
            .First(candidate =>
                candidate.Name.StartsWith(methodName, StringComparison.Ordinal)
                && candidate.GetParameters().Length == 0
                && candidate.ReturnType.ToString() == returnType
                && !candidate.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any()
            );
        return match.Invoke(target, null)!;
    }
}
