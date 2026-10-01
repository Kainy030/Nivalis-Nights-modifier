# PlayerHook

统一的 Player BepInEx IL2CPP 插件。输出 `PlayerHook.dll`，现在依赖四个钩子共用的 `NightsHack.HookRuntime.dll`；观察入口仍为只读，另提供显式启用的玩家数据探测和余额设置 IPC。

## 交付与构建

- 源码：`D:/NightsHack/src/NightsHack.PlayerHook/`
- 交付：`D:/NightsHack/outputs/PlayerHook/`
- 结构/逻辑存档：`D:/NightsHack/outputs/REFERENCE.md`
- 统一编译与检查：`./Build-Hooks.ps1`；原 `./Build-PlayerHook.ps1` 为兼容入口，也统一编译四个插件。推荐完整交付 outputs/Hooks；旧 outputs/PlayerHook 同步包含共享库。
- 基线：BepInEx **6.0.0-pre.2 Unity IL2CPP x64**、Il2CppInterop.Runtime **1.4.6.0**、HarmonyX 的 0Harmony **2.10.2.0**、目标 **net6.0**。沿用 MoneyHook 的本地依赖，没有升级框架。
- 生成器：`work/Generate-PlayerCatalog.ps1` 只读 DummyDll 与 script.json，输出静态签名清单；**运行插件不加载 DummyDll**。清单变更需复查排除项、测试和 REFERENCE 附录。

首次需要还原资产时：

```powershell
./Invoke-Dotnet.ps1 restore ./src/NightsHack.PlayerHook/NightsHack.PlayerHook.csproj
./Invoke-Dotnet.ps1 restore ./tests/NightsHack.PlayerHook.Checks/NightsHack.PlayerHook.Checks.csproj
./Build-PlayerHook.ps1
```

本轮初次还原因 nuget.org 漏洞数据服务不可达而报 NU1900；随后用已缓存的引用包还原成功，命令为 `restore <project> --ignore-failed-sources '-p:NuGetAudit=false'`。这是单次离线还原参数，不是已通过在线漏洞审计；没有更改依赖版本或永久关闭审计。构建脚本使用 `--no-restore`。

## 公共运行库迁移

PlayerHook 与 WorldHook、ItemHook、GameRuntimeHook 均继承 ObservationPlugin。原重复的 Catalog.cs、Observations.cs、NativeSnapshots.cs 和安装回调实现已归并到 HookRuntime；Player 的字典/技能/锁/knowledge 快照保留为单独启用的只读扩展。324 个候选方法与 32 个字段 schema 的清单字节未变。

公共观测类型统一为 NightsHack.HookRuntime.HookObservation、HookObservationBuffer、HookStatus；不保留旧 PlayerObservation 等类型的二进制兼容，使用这些显式类型的消费者需重新编译。Active、Observations、GetStatus、FieldDiagnostics 和 Installed 的读取入口保持。

## 包含的核心部分

18 个观察分组：生命周期、业务 Player、属性状态、场景角色、移动、交互、焦点、持物、摆放、动画、相机、环境、导航、库存、技能、Ghost、存档、输入。

`PlayerCatalog.json` 包含 **324 个候选方法、32 个字段 schema（338 个字段）、46 个明确排除的方法条目**。数字表示源码覆盖清单，不是游戏内成功安装/命中的数量。排除包括共享原生 RVA、泛型与静态方法；普通 getter、构造器和事件 add/remove 不在候选统计中。

- 按程序集、完整类名、方法名、参数、ref/out、返回类型匹配；要求实际生成的 interop 类型。
- 校验游戏 DLL/metadata SHA256，并通过已确认的 Il2CppInterop API 读取动态解析出的 MethodInfo 原生入口，与该版本 RVA 对照。**RVA 只校验，不据此写内存或直接安装钩子。**
- 各候选独立安装/拒绝；失败不会伪装成整组成功。每个入口拥有独立 Harmony ID；卸载仅处理自身补丁。
- Prefix/Postfix 均为 void；不修改参数、结果或游戏字段，不跳过原方法，不调用玩法修改方法，不吞掉原方法异常。
- 字段通过 IL2CPP 字段 API 读取，按字段名称、原生类型、value size 检查；失败项写入 `FieldDiagnostics`。没有固定对象偏移读取。
- 普通数值、bool、枚举、Vector2/3、Quaternion、九项 knowledge 有可读表示；字符串最多 160 字符；其他结构体为带类型的原生字节，引用为诊断地址。
- 属性和技能字典尝试通过**生成的字段访问器和只读 BCL 枚举器**读取，单次最多 32 项。投影不兼容时记录 `unavailable`，不编造值，也不调用游戏业务 getter。锁对象只展开当前对象持有的锁字段，不全局拦截通用锁工具。

## 修改器接入与只读观察

以下代码应在已加载本插件的游戏内控制组件中使用。进程外修改器通过 `NightsHack.Command.<PID>` 本地管道提交探测和余额设置命令；命令只在 `PlayerManager.Update()` 游戏线程回调中执行。

```csharp
var hook = NightsHack.PlayerHook.PlayerHook.Active;
if (hook != null)
{
    var status = hook.GetStatus();                // Installed != Observed
    var fieldErrors = hook.FieldDiagnostics;
    var latestByMethodAndPhase = hook.Observations.GetLatest();
    while (hook.Observations.TryDequeue(out var observation))
    {
        // 缓存/展示 observation；这里没有游戏对象，不能据此直接调用游戏方法。
    }
    long lostRecords = hook.Observations.DroppedCount;
}
```

`NightsHack.HookRuntime.HookObservation` 包含 Sequence、CallId、Before/After、Group、完整 Method、UTC 时间、托管线程 ID、实例地址与只读 Values。相同 CallId 对应一次采样调用的两阶段。Before 可在原方法抛出异常时单独存在；无 After 不等于已证明原方法抛异常，也可能是卸载、观察故障或队列丢弃。协程返回仅表示创建迭代器，不表示整段协程执行完成。

队列最多 512 条，满时丢最旧；Latest 最多每个候选方法两条，**不是每个实例一条**。没有强引用留住游戏对象。地址可能复用，不能当作长效句柄；Latest 是历史采样，不是无条件实时状态。

`Calls` 计有效回调次数，`Samples` 计成功写出 Before 的次数，`Faults` 计端点级观察异常。单个端点累计 3 次观察异常后停止采样，但原调用仍继续。字典/锁的局部读取失败另以 `unavailable` 记录；字段绑定失败另见 `FieldDiagnostics`。

## 配置与限制

配置文件 ID 为 `nightshack.playerhook`，由加载器生成：

- `Hook.Enabled=true`。
- `Groups.<分组名>=true`：18 组可独立关闭；加载时读取。
- `Observation.SampleIntervalMs=250`：仅用于清单 `Sampled=true` 的入口，范围 25–10000 ms；按方法全局采样，不按实例。完整标记见清单。业务事件通常不采样，但某些 `Update...` 名称入口也标记采样，不能假定捕获了每次变化。
- 没有逐帧磁盘日志/外部事件订阅者；只输出安装诊断和首个端点故障。

未按 LocalPlayer 过滤；回调可能属于多个同类型实例，包括保存对象。初始化前不会补建对象；字段引用未展开的容器、房产、订单等不等于已取得其完整内容。`MaxSpeed` 共享 getter 不钩，读其底层速度字段。`CancelOrder/PrePlayerTravel` 共享入口不钩；因此不是完备取消事件流。

`PlayerInventory.set_Money` 及 `_money` 前后快照已包含，用户已确认只保留 PlayerHook。独立 MoneyHook 的源码、测试、构建脚本和交付物已清理。当前余额设置命令在 PlayerHook 中接入原 setter，通过本地 IPC 排队并在 `PlayerManager.Update()` 的游戏线程回调中执行；命令事件同时写入 BepInEx 控制台和 hooks JSONL。`Commands.Enabled` 默认值为 `true`，因为 Trainer 的玩家探测和余额命令依赖该服务；已有配置若保留 `Enabled = false`，需改为 `true` 或删除 `BepInEx/config/nightshack.playerhook.cfg` 后重启游戏。金额以游戏整数单位传输，安全范围为 1–2147483646（对应用户界面 0.01–21474836.46）。调查报告 `outputs/money-investigation.md` 继续保留。旧接口的旧值/请求值/实际值，在这里对应同一 CallId 的 Before 字段、Before 参数和 After 字段；并非旧 MoneyObservation API 的二进制兼容替换。

“不更改游戏逻辑”表示观察器没有业务写入或原调用替换；安装补丁与读取必然有开销。**尚未验证加载器在该游戏的兼容性、324 个候选的实际可安装数量、生成的字典投影、原生回调命中、性能、保存往返或卸载效果。**

当前验证：Release 构建、15 项 Player 检查及 21 项跨插件/运行库检查，覆盖签名拒绝、核心目录、共享地址排除、哈希变化、队列、并发、采样和编译后回调形状。不是游戏内集成测试。DLL 不能直接运行或独立附加 PID；本轮没有部署加载器或写入游戏目录。
