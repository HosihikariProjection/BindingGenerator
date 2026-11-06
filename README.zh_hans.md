# Hosihikari.Bindings.Generator

[English](README.md) | [简体中文](README.zh_hans.md)

生成器把版本化 IR 转成 C ABI **绑定垫片** 与原生风格 C# **Interop**。它不产出 .NET 风格的手写 Idiomatic 层。

## 边界

- **输入**：libTooling 前端产出的 IR 分片，以及 IR 库契约。
- **输出**：生成的 C++（绑定垫片）与 C#（Interop）源码。两者提交在绑定仓，并由 CI 重跑比对。
- **不在这里**：手写 Idiomatic 包装、HostingShim、CLR 寄宿。
- IR 库是并列 checkout（`../Ir`）；CI 用 `actions/checkout` 指定 `repository`/`path` 拉取。

## 工程

| 路径 | 用途 |
|---|---|
| `src/Hosihikari.Bindings.Generator` | .NET CLI：version、IR 校验、覆盖校验 |
| `frontend/` | libTooling CMake 目标（LLVM 23.1） |
| `tests/fixtures/` | CLI 契约与真实 shim 作业使用的 IR fixture |
| `tests/BindingShimSmoke` | LLAPI 切片的 Windows 端到端 smoke test |
| `tests/RealShimSmoke` | link-carrier fixture 的 CI smoke test |

## 命令

```shell
dotnet run --project src/Hosihikari.Bindings.Generator/Hosihikari.Bindings.Generator.csproj -c Release -- version
dotnet run --project src/Hosihikari.Bindings.Generator/Hosihikari.Bindings.Generator.csproj -c Release -- validate-ir tests/fixtures/basic/ir-manifest.json tests/fixtures/basic
dotnet run --project src/Hosihikari.Bindings.Generator/Hosihikari.Bindings.Generator.csproj -c Release -- emit tests/fixtures/basic/ir-manifest.json tests/fixtures/basic artifacts/emitted --abi-header ../Binding.Abi/native/hosihikari_abi.h
```

`validate-ir` 读取 manifest 与分片目录下所有 `*.jsonl`，然后运行 IR 校验器。`validate-coverage` 对 `coverage/summary.json` + `coverage/manifest.jsonl` 做同样的事。

`emit` 写出 `interop/`（C#）、`shim/`（C++ 绑定垫片）与 `coverage/`（manifest + summary）。传入 `--native-root <path>` 会为 IR 的源文件发射真实绑定垫片：

```shell
dotnet run --project src/Hosihikari.Bindings.Generator/Hosihikari.Bindings.Generator.csproj -c Release -- emit `
  artifacts/llapi-full-ir/ir-manifest.json artifacts/llapi-full-ir artifacts/llapi-real-shim `
  --native-root C:/path/to/LeviLamina --abi-header ../Binding.Abi/native/hosihikari_abi.h --link-carrier --ll-allocator
```

`--abi-header` 指向 `HosihikariProjection/BindingAbi` 的 `native/hosihikari_abi.h`；发射器把它复制到每个 shim 目录，使 shim 构建保持自包含。生成的 C# 引用 `Hosihikari.Bindings.Abi` 提供的 `StringView`、`ArrayView`、`SharedHandle` 与负责版本校验的 `ShimLibrary` 加载器。

不传 `--native-root` 时写出 fixture CI 使用的契约 stub。

### 真实绑定垫片 v1

- C ABI 是每个 carrier surface 一份版本化的 `HosihikariBindingTableV1`。句柄为 `void*`；拥有句柄由构造函数工厂产生，由生成的释放器回收。函数表字段使用函数身份的稳定命名（如 `hs_method_1a2b3c4d`），而不是槽位序号；序号仅保留为物理数组位置，因此新增函数不会重命名既有调用点。字段字符串以借用 `HosihikariStringView` 返回，托管侧立即复制；字符串参数为 UTF-8 的 `(const char*, size_t)` 对。
- 投影构造函数、析构函数、公共字段与受支持的方法。生成器有意拒绝的成员会发射为带 `[Obsolete]` 标记的 `NotSupportedException` stub，标记消息包含同样的原因；调用时产生编译器警告 CS0618 并在 IDE 中显示删除线，项目可用 `<WarningsAsErrors>CS0618</WarningsAsErrors>` 将其升级为错误。构造函数与方法接受多个参数。构造函数可以接受指向有生成包装的公有声明的可变引用与右值引用，shim 通过解引用地址传入这些参数。方法可返回 `void`、基础类型、枚举、不透明指针、字符串或符合条件的按值 record；指向公有声明的一层 `T*`/`T&` 返回会投影为类型化借用包装（`T?`/`T`），其 `Dispose` 只使包装失效，`ReleaseHandle` 拒绝借用句柄，所有权仍归原生侧；`optional_ref<T>` 返回沿用同一包装路径与隐藏存在标志；公开的 `(nint handle)` 构造函数仍是接管裸句柄的拥有路径；指向公有声明的一层指针参数使用可空包装 `T?`，null 映射为空指针；右值引用参数保持非空包装，更深的间接层级保留 `nint`；可访问的公有基类（含传递基类）生成 `As<TBase>()` 上转型，多态基类为每个传递派生类生成 `TryAs<TDerived>()` RTTI 下转型，两者都返回借用包装；字符串结果被复制到 shim 的线程局部缓冲区，托管包装立即复制。每次调用可以固定多个字符串参数；不超过 256 字节的 UTF-8 编码使用 stackalloc 缓冲区，避免每次调用的 byte[] 分配。枚举使用底层整数 ABI。按值 record 参数由调用方句柄解引用传入；按值 record 结果复制为 shim 持有、托管包装可释放的对象。record 与容器字段以借用地址视图暴露；数组字段只生成 getter。`std::vector<T>` 结果通过借用 `HosihikariArrayView` 返回 `T[]`/`string[]`/`nint[]`，包括指针元素，空结果返回 `Array.Empty<T>()`；vector 参数展开为隐藏的 `(元素指针, 数量)` 对，托管侧以 `T[]`/`string[]`/`Variant[]` 传入，支持 record 与 variant 元素，每次调用支持多个 vector 参数，每个 vector 使用独立固定的元素视图；`std::initializer_list<T>` 参数仍不投影，因为 C++ 无法构造运行时长度的 initializer list，相关类已有等价的 vector 重载；`std::vector<std::pair<string, uint64>>` 参数以 `KeyValuePair<string, ulong>[]` 传入，字符串键/值共享单个 UTF-8 arena；`std::optional<T>` 参数与结果通过隐藏的存在标志传递，托管侧使用可空标量、`string?` 或拥有的 `T?`；`std::unique_ptr<T>` 结果以释放所有权的方式返回拥有的 `T` 句柄；`unique_ptr` 参数从包装接收所有权，包装在调用前释放句柄，重复使用会抛异常；带 public 虚析构的抽象接口在此路径下也会生成释放器。`std::shared_ptr<T>` 结果返回 `SharedHandle`，持有一次共享引用并在释放时归还；`shared_ptr`/`weak_ptr` 参数接受 `SharedHandle`，shim 通过带类型校验的 holder 恢复类型化引用。`optional_ref<T>` 参数与结果复用 optional 存在标志，传递借用引用；托管侧为 `T?`/`nint?`。`nonstd::expected_lite::expected<T, E>` 结果返回 `T`，失败时抛出携带原生错误消息的 `InvalidOperationException`；`E` 可为 `ll::Error` 或字符串。`std::variant<T...>` 在 alternative 为字符串、标量/枚举、指针或可复制 record 时生成拥有包装，提供各 alternative 的 `Create`/`As`/`Is`、`Index` 与 `Dispose`；by-value 参数复制，`const&` 参数借用，by-value 结果复制为拥有句柄。成员全部继承（没有字段、构造函数或析构记录）的公有可复制类也可作为 record alternative；生成器会补齐其 `Dispose` 所需的删除函数。`endstone::NotNull<T>`/`Nullable<T>` 返回使用 `SharedHandle`；序列返回值区分借用指针元素（`nint[]`）、拥有元素（record/`unique_ptr` 为 `T[]`/`T?[]`）与共享元素（`shared_ptr`/`NotNull`/`Nullable` 为 `SharedHandle[]`）；`unordered_set<T*>` 返回借用地址数组。`std::chrono::time_point<std::chrono::system_clock>` 映射为 Unix 毫秒；其他 clock 映射为原生 tick 计数；`std::chrono::duration` 映射为原生 count；optional 时间值使用 `long?`。返回的 `std::function<R(Args...)>` 生成带 `Invoke`/`Dispose` 的可调用包装。variant、可调用、chrono、`typeid_t`、`strong_ordering` 与 filesystem path 的引用返回值也按值投影复制，而不是退化为不透明句柄；指向这些类型的指针仍为借用句柄。`Bedrock::typeid_t<C>` 映射为 `ushort`，`std::strong_ordering` 映射为 `sbyte`（-1/0/1），`std::filesystem::path` 映射为 UTF-8 字符串；optional 形式沿用标量/字符串规则。`std::unordered_map<K, V>` 参数与结果使用托管 `Dictionary<K, V>`；键支持字符串/标量/指针，值支持字符串/标量/指针、record 复制与 variant 复制。map 参数复用 vector 的 arena 策略：字符串键与值在每次调用中共享单个 UTF-8 arena，不超过 16 个条目的 map 使用 stackalloc 的 `MapEntry` 缓冲区。条目类型无法投影时，引用与指针结果仍返回借用句柄。
- 枚举声明投影为 C# `enum` 类型。底层类型取自 native 的 `underlyingType`，只有值放不下时才加宽；每个枚举值带上求值结果（IR 1.6.0 的 `member.value`），隐式值与表达式值都原样到达。嵌套枚举沿用嵌套类规则，落入 `_Nested` 命名空间段；清洗后重名的枚举值追加稳定短哈希，关键字用 `@` 转义。签名仍按底层整数投影，调用时写成 `(int)Foo.Bar`。
- 没有任何可用成员的类型（没有可用的构造、析构、字段访问、方法或转型，或没有任何枚举值的枚举）仍然发射以保留类型身份，并带 `[Obsolete(message, false)]`；生成文件内部禁用 CS0618，因此标记只在用户代码里触发。
- 默认 link-safe 模式投影 public、非模板、源文件存在于 native root、所有方法 inline 且基类 link-safe 的类。`--link-carrier` 另外接纳 public 的具体模板实例，并链接 carrier import library、fmt 与 prelink 生成的 Bedrock 导入。模板实参必须可访问；不完整实参只对标准库或受支持的第三方类型放行。成员模板、右值限定成员与变量模板不投影；模板析构函数需有可见定义才生成释放器。赋值运算符对非模板类投影；模板实例的赋值运算不投影，因为底层操作可能需要完整的模板实参，或已被删除。静态方法与静态字段不传 `self`。抽象类不生成工厂。持有 `string_view` 或 `std::string` 引用字段的类使用 holder 支撑的工厂：shim 把字符串参数复制到 holder，再基于这些副本构造对象，视图与引用在包装释放前保持有效。只有一个纯虚回调（参数为指针/引用/基础类型，返回 `void`/基础类型/枚举）、public 虚析构、且构造函数仅含基础类型/枚举/共享指针或为隐式默认构造的抽象类会生成 director 工厂：shim 继承该接口，把虚调用转发到 `(invoke, userData, release)`，并返回 `shared_ptr` 句柄。每个生成的包装还提供 `Borrow(nint)`，用于以非拥有方式查看裸句柄。命名空间级 public 函数投影为每个原生命名空间一个 `Functions` 静态类。inline 函数直接编进 shim；`--link-carrier` 下，载体导出的 out-of-line 函数改为通过导入库调用。返回与参数使用与成员相同的映射（标量、字符串、指针、按值 record、`optional`/`optional_ref`、`unique_ptr`、`expected`、vector、map、共享句柄、回调）。命名空间级比较运算符（`==`、`!=`、`<`、`<=`、`>`、`>=`）投影为 `operator_equal` 形式的静态方法并通过 ADL 解析；其他运算符、函数模板、依赖类型的 friend 模式、lambda 参数与多级指针保持不投影。
- 每个表函数都是 C++ function-try-block。原生异常被捕获到由 `hosihikari_get_last_error` 暴露的线程局部消息中，C++ 异常不会跨越托管边界。工厂失败返回空句柄，生成的 `Create` 抛出带该消息的 `InvalidOperationException`。
- `std::function<R(Args...)>`、`brstd::move_only_function<R(Args...)>` 与 `brstd::function_ref<R(Args...)>` 参数暴露为 `(invoke, userData, release)` 三元组。shim 用共享回调状态包装成对应的可调用类型（`std::function`、`brstd::move_only_function` 或 `brstd::function_ref`）；最后一个副本销毁时调用 `release(userData)`。v1 支持基础类型/枚举返回，参数支持基础类型、枚举、字符串、指针、引用与按值 record；`const` 标量、枚举与字符串引用按值传递。
- `--ll-allocator` 把 shim 的全局 `operator new`/`delete` 交给 `ll::memory::getDefaultAllocator()`。LeviLamina 等替换全局分配器的 carrier 需要此开关；mock carrier fixture 不启用。
- `NativeMethods.Load(path)` 在使用前校验导出的表版本与 `sizeof(Table)`；表的前两个字段是 `version` 与 `size`。

`frontend/build-shim.ps1` 用 clang-cl 与 `/MD` 针对 LeviLamina 的 include 环境编译生成的 shim。用 `-AdditionalLinkLibrary` 传入载体/包库，用 `-DelayLoadLibrary LeviLamina.dll` 构建可独立加载的 smoke 版本。`frontend/build-carrier-shim.ps1` 先编译对象，运行 `prelink` 生成 Bedrock runtime 导入库，再链接 carrier shim。`frontend/smoke-shim.ps1` 发射、构建并运行 `tests/BindingShimSmoke`；加 `-LinkCarrier` 会使用 carrier 构建与 delay-load。

## 本地 LLAPI 切片

`frontend/run-local.ps1` 解析 `ll/api/event/Event.h`，把 IR 写到 `artifacts/llapi-ir` 并校验；`frontend/run-umbrella.ps1 -Module ll/api` 解析完整的 `src/ll/api` + `src-server/ll/api` 伞头，复用 LeviLamina 的 `compile_commands.json`，当前在 IR 1.6.0 产出 1,532 声明 / 6,515 成员。被过滤签名按值引用的公有 record 类型即使头文件位于模块过滤之外，也会连同构造、析构与字段一起写入 IR。IR 为每个函数与方法记录 `inline`/`defined`/`consteval`，并记录成员模板、引用限定符、枚举底层类型与类特性（`abstract`、`copyConstructible`），绑定垫片规划器据此判断资格与所有权。

```shell
powershell -File frontend/run-umbrella.ps1 -Module ll/api -Output artifacts/llapi-full-ir
powershell -File frontend/smoke-shim.ps1 -IrDirectory artifacts/llapi-full-ir
powershell -File frontend/smoke-shim.ps1 -IrDirectory artifacts/llapi-full-ir -LinkCarrier
```

完整 LLAPI 伞头当前选出 496 个 link-safe 函数；用 `--link-carrier` 链接 `LeviLamina.lib` 时为 3,819 个函数。切片覆盖 `ll::event::EventId`、`ll::event::EventIdView`、`ll::hash_utils::HashedIdBase`、`ll::hash_utils::HashCombiner`、`ll::data::Version` 以及其他符合条件的类。Smoke test 从托管字符串创建原生 `EventId`，读取名称与哈希，比较两个 `HashedIdBase` 视图，调用 `HashCombiner::hash()`，并让 `ll::data::Version` 经过字符串构造、`to_string()`、字段读取、`valid(string)` 与 `isIdenticalTo(Version)` 往返。其余成员保留 Interop stub，并在覆盖清单中只记录 `interop` 发射器。

## Endstone 互操作面

`frontend/run-endstone.ps1` 解析 Endstone 公共伞头（`include/endstone/endstone.hpp`）并提供 expected-lite 与 nlohmann_json 头。`--virtual-dispatch` 为 header-only 虚接口发射免链接 shim：inline 成员与虚方法经载体提供的 vtable 调用，非虚 out-of-line 成员保留 stub。`frontend/build-endstone-shim.ps1` 用 Endstone 头编译生成的表。Endstone 0.11.14 互操作面当前选出 2,364 个函数，包含符合条件的模板实例、inline/constexpr 静态字段、非模板类的赋值运算符与继承转换。

`frontend/run-endstone.sh` 在 Linux 上生成 `linux-x64-server` 配置。`endstone-linux-ir` CI 作业构建针对 LLVM 23.1 的前端，生成并校验 IR，发射 Interop 与 shim，将发射出的 Interop 与已提交的 Windows-IR 输出逐字节比对，并编译 shim。固定宽度类型的拼写、标准库迭代器类型与 `system_clock::time_point` 的 duration 会归一到 Windows 形态，使声明 id、函数表字段哈希与覆盖在平台间保持一致（ADR 0018）。

## 载体更新

`frontend/new-carrier.ps1` 端到端完成一次载体升级：解析 `Third/` 下的检出（`-Fetch` 时补建 git worktree）、校验 Bedrock 头文件列表并可按需裁剪载体已删除的路径（`-PruneHeaderList` 会把文件名唯一的路径重指向实际位置，且从不覆盖输入列表）、在 `llapi` 面需要 `LeviLamina.lib` 时用 xmake 构建载体、运行前端与发射器、对照检出校验已提交的来源信息，并可选地在真实服务器里跑运行期 smoke。

```shell
# 预览：只解析载体并校验头文件列表
powershell -File frontend/new-carrier.ps1 -Carrier levilamina -Version v26.61.0 -Fetch -DryRun

# 从重新构建的载体生成 Bedrock 与 LLAPI
powershell -File frontend/new-carrier.ps1 -Carrier levilamina -Version v26.61.0 -Fetch -BuildCarrier

# 只生成 Endstone
powershell -File frontend/new-carrier.ps1 -Carrier endstone -Version v0.11.16 -Fetch -Surface endstone
```

`frontend/carriers.json` 记录各互操作面当前发射所用的载体版本。`carrier-canary` 工作流每周一对照上游最新的 `vX.Y.Z` tag，发现更新时创建或更新带 `carrier-update` 标签的 issue，正文给出对应的 `new-carrier.ps1` 命令。

脚本不处理产品层面的收尾：包 `<Version>`、README 与 profile 的覆盖数字、覆盖基线以及 `frontend/carriers.json` 本身。

## 相关仓库

- `HosihikariProjection/BindingIr`：schema、模型、读写器与校验器。
- `HosihikariProjection/BindingAbi`：共享 ABI 值类型、加载器与原生头文件。
- `HosihikariProjection/EndstoneBinding`：Endstone 互操作面。
- `HosihikariProjection/LeviLaminaBinding`：LeviLamina 互操作面。

## 许可证

GPL-3.0-or-later，见 `LICENSE.md`。
