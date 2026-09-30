# HookRuntime：四个观察钩子的公共运行库

PlayerHook、WorldHook、ItemHook、GameRuntimeHook 四个 BepInEx IL2CPP 插件均继承公共 ObservationPlugin，共用 `NightsHack.HookRuntime.dll` 的安装/卸载、签名校验、回调分发、字段读取、采样和快照队列实现；按完整签名及原生 RVA 排除重复。没有写值、UI、IPC、热键或游戏部署。

| 插件 | 职责 | 静态候选数 |
|---|---|---:|
| PlayerHook | 玩家、属性/技能、移动/交互、金钱与玩家业务 | 324 |
| WorldHook | 场景、旅行、Ghost/空间索引、世界视图、任务对象、地产经营 | 350 |
| ItemHook | 定义/单件/堆栈/库存、腐坏、拾取摆放、家具、经营库存 | 220 |
| GameRuntimeHook | 时间、AI 模拟、天气光照、存取档、对象池 | 173 |

这些是候选数，不是原生安装或命中数。构造/终结、共享 native body、泛型、抽象声明、普通访问器、编辑器/显式接口包装等有明确排除记录。调查过的 202 条方法记录均能对应候选或排除，不是 202 条全部安装。

## 构建与交付

- `D:/NightsHack/Build-Hooks.ps1`：编译四个插件、公共运行库并运行两套检查，成功后统一打包到 `D:/NightsHack/outputs/Hooks/`。旧 Build-PlayerHook.ps1 / Build-WorldHooks.ps1 转发到此入口；旧交付目录同步刷新。
- 首次需要还原时使用 `./Build-Hooks.ps1 -Restore`。本次已用缓存及单次 `--ignore-failed-sources '-p:NuGetAudit=false'` 还原；未执行在线漏洞审计，未更改依赖版本。
- 沿用 net6.0/x64、BepInEx 6.0.0-pre.2、Il2CppInterop.Runtime 1.4.6、HarmonyX 2.10.2。
- 四个插件共用一份 `NightsHack.HookRuntime.dll`，它是普通依赖库，不另注册为游戏插件。仅复制某个插件 DLL 而漏掉共享库不能视为完整交付。依赖的 BepInEx/Interop/Harmony 来自既定加载器。
- 此脚本不安装加载器，不复制任何文件到游戏目录。

## 观察接入

```csharp
var hook = NightsHack.ItemHook.ItemHook.Active;
if (hook != null)
{
    var status = hook.GetStatus();
    var diagnostics = hook.FieldDiagnostics;
    while (hook.Observations.TryDequeue(out var sample))
    {
        // sample 是脱离游戏对象的诊断数据，不能用其地址作为永久句柄。
    }
}
```

PlayerHook、WorldHook 与 GameRuntimeHook 提供相同入口。四者统一使用 NightsHack.HookRuntime.HookObservation / HookObservationBuffer / HookStatus，旧 PlayerObservation 类型已合并；使用 var 的常规读取方式不变。该 API 面向同进程组件，进程外修改器仍需后续 IPC。

每次采样包含 Before/After、插件内 CallId、完整方法签名、线程/时间、参数、结果和可读取的实例字段。不同插件的 CallId 不可当作全局唯一值。静态方法实例地址为 0，不读取静态字段。引用仅记录地址，容器不自动展开为完整物品清单，复杂结构可能以字节或 opaque 表示。

每插件队列 512 条，满时丢最旧；Latest 按方法/阶段保留，不按实例。Calls 统计回调，Samples 统计写出 Before，Faults 统计观察错误；端点累计 3 次错误停止采样。高频入口默认 250ms、跨实例采样，MoveNext 同样采样，因此不是无损阶段日志。原方法异常等情形可能只有 Before。

不调用 `ItemEntity.get_Item` 等业务 getter 来取快照；只在游戏自身调用时观察该入口。销毁/回收/加载协程等清单标记的入口跳过 After 实例字段读取，保留 Before 的诊断地址。记录仍不保证构成完整事务，也不自动归属于 LocalPlayer。

PlayerHook 单独启用公共库的 Player 快照扩展：保留最多 32 项的属性/技能字典、knowledge 标志、PlayerStat 参数及持有/返回锁的读取。只调用校验过的生成字段访问器与只读集合枚举，不调用游戏业务 getter。其余三个插件不启用此扩展。各插件的队列、采样器、状态和卸载归属仍各自独立。

## 增加物品接口预留

```csharp
using NightsHack.ItemHook;

IBackpackItemAddition service = ItemHook.BackpackItems;
var request = new AddBackpackItemRequest(Guid.NewGuid(), itemTypeGuid, quantity);
AddBackpackItemResult result = await service.AddToBackpackAsync(request, cancellationToken);
// 当前 IsImplemented=false；有效请求返回 NotImplemented、AddedQuantity=0。
```

目标契约为当前本地玩家背包，ItemTypeGuid 指定义标识，不是物品名称、世界实体 ID 或内存地址。RequestId、非空标识与正数量会校验；取消返回 Cancelled；无排队、无游戏对象解析、无物品创建。不要把该预留接口当成已经能刷物品。

未来实现需要解析实际 ItemType 与 LocalPlayer.PlayerInventory，再在游戏线程走已确认的原生业务入口，核对限制、实际接受数量、部分成功与通知；价格/单件价值语义、重复 RequestId、防止重复执行、场景切换和 IPC 尚需实现。详见 HOOKS-REFERENCE.md。

## 验证边界

加载时校验两个游戏输入哈希、原生类身份、完整方法签名、静态/实例与 out/ref、动态解析 MethodInfo 对应 RVA。版本或投影不符就拒绝该入口。协程名字被 interop 改写时按原生嵌套类型身份匹配，不猜生成后的名称。

Release 编译 0 警告/错误，Player 检查 15 项及跨插件/运行库检查 21 项通过；检查涵盖目录与原始元数据的一致性、与 PlayerHook 去重、关键方法/协程、队列/采样、回调形状和预留接口不写入。尚未验证加载器发现、Harmony 原生 patch、真实回调、所有字段投影、游戏线程行为、性能或卸载。`Installed` 也只表示 patch API 接受，不等于游戏内逻辑验证。

完整归属/排除：每个插件的 `*.Catalog.json` 与 `hook-coverage-audit.json`。结构和设计档案：`D:/NightsHack/outputs/HOOKS-REFERENCE.md`；原始逆向报告：`D:/NightsHack/outputs/world-investigation.md`。
