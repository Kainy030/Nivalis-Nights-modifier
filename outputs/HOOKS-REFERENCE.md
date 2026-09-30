# 四个钩子与公共 HookRuntime

范围更新（2026-09-30）：完整 1,067 个目录入口恢复为被动资料，含 AI/视觉；默认观察选择为空，不初始化原生目标/字段/定时器。仅 Features.AllowList 或独立 Diagnostics.Enabled + 精确 AllowList 可安装观察。ResolveTarget 按需校验解析，不自动 patch 或调用游戏。旧安装数与实测为历史状态；本版本尚未游戏内验证。


更新：2026-09-30。项目 `D:/NightsHack`，目标 Nivalis Nights，IL2CPP/net6.0/x64。

## 本轮结果

**PlayerHook、WorldHook、ItemHook、GameRuntimeHook** 四个插件现统一继承 `NightsHack.HookRuntime.ObservationPlugin`。安装、校验、回调、采样、字段读取和观测数据类型均共用，Player 专用快照按配置保留。四个插件只观察原调用，不跳过/替换原方法、不写参数/返回值、不改游戏字段。

新增背包加物品契约 `IBackpackItemAddition`，当前是明确拒绝执行的预留实现。它不创建物品、不排队、不解析游戏实例；有效请求返回 `NotImplemented` 和 `AddedQuantity=0`。

当前已完成 **Release 编译、41 项托管/静态检查及有限游戏内实测**。本机部署后隔离了启动和读档崩溃，最终安装 1,019/1,067 个候选，48 个明确拒绝；四组都有真实回调。详细证据及尚未验证内容见 [运行实测报告](HOOK-RUNTIME-TEST.md)。统一交付目录：[Hooks](D:/NightsHack/outputs/Hooks)；构建入口：[Build-Hooks.ps1](D:/NightsHack/Build-Hooks.ps1)。旧构建入口转发到统一构建，并同步更新旧交付目录。

## 范围与 PlayerHook 对照原则

范围来源是上轮调查的重点类型，加上明确涉及的 Player 物品入口、ManagersSave、LightCycleManager；不是把 663 个关键词命中全部装钩。重点类型的方法按实际元数据进一步展开；**202 条已提取原生指令的方法记录**与**本次更多的元数据候选方法**不是同一种验证深度。

去重流程：完整类型/方法/参数签名匹配 PlayerCatalog → 比较 Player 已占用原生 RVA → 检查全映射共享 RVA → 在三个新插件之间统一分配。Player 的字段 schema 只是读字段，不代表相应类型的方法已被钩住。例如 PlayerHook 有 Ghost 字段布局，GhostManager 注册/迁移仍需要 WorldHook。

PlayerInventory.AddItem、AddAllFurniture、ClearItems 与 PlayerObjectHolder.StoreEntity 保持在 PlayerHook，不重复加入 ItemHook。下层 ItemContainer、ItemStack、ItemEntity 并未因此重复：它们既是调用链更深层，也是其他库存来源会使用的入口。一次游戏行为可能产生多个层级的观察记录，不应直接按记录条数计算获得物品数量。

本次重构改变 PlayerHook 的外层实现和二进制依赖，方法清单字节保持不变（SHA256：`40FB6DCAF34E7C3ECAD163714A44525EBC523C9B0DAAA27FE1A0E78DAEC79AAA`）。最新 DLL 哈希见 outputs/Hooks/build-sha256.json。四个插件总计 1,067 个互不重复的候选入口。

## 分类

| 插件 | 分配的职责 | 关键方法 |
|---|---|---|
| PlayerHook | 玩家属性/技能、移动、交互、金钱/背包玩家入口 | SetStat、AddExperience、set_Money、AddItem、TeleportPlayer |
| WorldHook | 场景/旅行、Ghost、空间注册、视图、任务对象、地产/经营设施 | LoadArea、RequestTravel、MoveNext、RegisterGhost/FlushGhostOperationQueues、TransferGhostToScene、UpdatePosition、WorldObjectGhost.Use |
| ItemHook | 物品定义/单件/堆栈/容器、腐坏、库存存取、实体拾取、摆放/家具、VenueInventory | SafeCreate、TryAdd 五重载、TryCreateInInventory、TryTake、UpdateDecay、Store、PickUp/Place、set_CurrentProperty |
| GameRuntimeHook | 跨系统的时间、AI 模拟、天气/光照、序列化、池化 | AddTime、SkipTime、UpdateWeatherForCurrentTime、SerializationManager.Save/LoadRoutine.MoveNext、GameObjectPool.Get、Pool.TryPop/Release |

规则边界：地产/经营设施本体归 WorldHook；其中 VenueInventory 归 ItemHook。Ghost 的生命周期归 WorldHook；AgentGhostSimulator 调度归 GameRuntimeHook。物品自己的 Save/Load 归 ItemHook；全局 SerializationManager 归 GameRuntimeHook。HookRuntime 是四个钩子的公共依赖库，不另注册为插件。

## 观察架构与限制

`具体插件 → 嵌入的 Catalog → 动态 runtime interop 类/方法 → 完整签名和 native RVA 校验 → Harmony 前后观察 → 分插件有界快照队列`

- 使用生成的 runtime interop，不引用或部署 DummyDll；DummyDll 仅用于生成清单和静态检查。
- 保留游戏输入双哈希锁；RVA 仅做解析后的交叉校验，不作为固定偏移写值。
- 当前桥接兼容性边界：拒绝 ref/out、投影原生值类型返回、1/2/4/8 字节的投影原生值类型按值参数及已复现崩溃的 HoldableEntity.Update；保留原方法。完整候选表不等同于已安装表。最终配置无临时 RVA 排除，具体运行状态见 GetStatus 或诊断导出。
- 支持静态入口：单独回调形状，不注入不存在的 `__instance`。静态样本实例地址为 0，未快照静态字段。
- 支持七个实际协程 MoveNext；对 interop 改名的嵌套类，核对原生类名再选择，不把“拿到 IEnumerator”误认为协程完成。仍有默认采样，不保证每个状态跳转都有记录。
- 字段通过 IL2CPP 字段 API 读取，引用不展开为库存清单，不调用业务 getter，不调用用户自定义 ToString。复杂结构为原生字节或 opaque。
- 销毁/回收/卸载/Store/Use/交互以及已标记的加载协程，跳过 After 实例字段解引用；Before/After 保留同一 CallId 与进入时地址。地址仍只是诊断值，不是活对象句柄。
- 每插件 512 条队列；Latest 按方法/阶段留样，高频入口默认 250ms 跨实例采样。库存增减和腐坏业务入口不按该时间间隔采样。原方法异常、停用或观察失败可能只有 Before。
- 端点 3 次观察错误后停止采样；字段绑定失败有独立诊断。状态区分 Rejected、Failed、Installed、Observed、ObservationDisabled、Unloaded/CleanupUncertain。
- 生命周期卸载失败时保留运行库中的占用记录，避免盲目再次安装；这不表示原生卸载已在游戏内验证。
- Shared/空函数、构造/终结、开放泛型、抽象声明、普通访问器、编辑器/编译器包装、显式接口包装、未支持的 ABI 等均明确排除，不凑数宣称覆盖。

`GhostManagerSave.Save/Load` 的空体仍排除；全局序列化入口和 Ghost.Save/Load 在相应插件观察。`ItemEntity.get_Item` 会懒创建数据：这里只观察游戏自然调用，快照器不主动调用它。

## Player 迁移与 API

PlayerHook 现在与其他三个插件共用安装引擎及观测类型。324 个候选、32 个字段 schema、18 个分组保持；属性/技能字典最多 32 项、knowledge、PlayerStat 参数和锁对象快照仍由公共库的 Player 扩展提供，仅 PlayerHook 启用。当前通过 15 项 Player 检查和 26 项跨插件/运行库检查；有限游戏内实测见报告，未覆盖全部玩法或字段。

公共记录统一为 NightsHack.HookRuntime.HookObservation / HookObservationBuffer / HookStatus。原 PlayerObservation 等类型已移除，显式引用旧类型的消费者需要重新编译；常用 Active、Observations、GetStatus、FieldDiagnostics、Installed 入口保留。各插件仍有独立队列与 CallId；卸载自身不会主动卸载其他插件。

## 未来增加物品到背包的接口

源码：[BackpackItems.cs](D:/NightsHack/src/NightsHack.ItemHook/BackpackItems.cs)。公开入口为 `NightsHack.ItemHook.ItemHook.BackpackItems`，类型 `IBackpackItemAddition`，契约面向**执行时的当前本地玩家背包**。

```csharp
var request = new AddBackpackItemRequest(
    RequestId: Guid.NewGuid(),
    ItemTypeGuid: itemTypeGuid,
    Quantity: amount);
var result = await ItemHook.BackpackItems.AddToBackpackAsync(request, cancellationToken);
```

| 契约项 | 含义 |
|---|---|
| RequestId | 请求关联标识；不是已经实现的去重/幂等存储 |
| ItemTypeGuid | 物品定义标识，未来映射实际 ItemType.Guid；不是显示名称/ArticyGuid/世界实体地址 |
| Quantity | 请求的正整数件数 |
| IsImplemented | 当前固定 false |
| Status | 当前可能 NotImplemented、InvalidRequest、Cancelled；未来保留 Unavailable、Failed、Partial、Succeeded |
| RequestedQuantity / AddedQuantity | 请求数量和实际接受数量；当前 AddedQuantity 恒为 0 |
| Detail | 未实现或校验/取消原因，不伪报成功 |

该接口当前无事件委托/可注册执行器，避免有人误把保留接口接成隐式写入。未来修改器可以实现该接口并接入明确的命令服务；本轮不开发该服务。重复有效请求仍是无副作用的 NotImplemented，不能由此宣称未来执行的幂等性已经实现。

已有真实入口与后续实现决策：

1. 解析 ItemType 定义与执行时的 LocalPlayer/PlayerInventory，验证归属和实例有效性；不能选“最近一次回调出现的任意 ItemContainer”。
2. 已有 `PlayerInventory.AddItem(ItemType,int)` 由 PlayerHook 观察，但返回 void；不能用正常返回推断物品全部加入。
3. ItemHook 包含 `ItemContainer.TryCreateInInventory(ItemType,int,int pricePerItem)`（`0x3058DC0`）和 TryAdd 各重载，能观察更底层创建/入库。未来需确认玩家入口的业务副作用，以及 pricePerItem 的真实赋值策略，不能现在随便设 0 或跳过玩家路径。
4. 原生结果 `Failed=0 / Partial=1 / Succeeded=2` 必须保留，不能当 bool。未来核对实际容器变化、容量限制、剩余物品和通知后再返回 AddedQuantity；接口自己的状态枚举不是这个游戏枚举的数值替身。
5. 实际命令必须调度到游戏线程，明确处理加载中/玩家不可用、取消时点、重复 RequestId、超大数量、对象失效、结果回传和进程外 IPC。不要在 Harmony 观察回调里顺手发放物品造成重入。

## 源码与可再生证据

- [WorldHook](D:/NightsHack/src/NightsHack.WorldHook/WorldHook.cs)、[ItemHook](D:/NightsHack/src/NightsHack.ItemHook/ItemHook.cs)、[GameRuntimeHook](D:/NightsHack/src/NightsHack.GameRuntimeHook/GameRuntimeHook.cs)。
- [共享观察引擎](D:/NightsHack/src/NightsHack.HookRuntime/ObservationPlugin.cs)、[README](D:/NightsHack/src/NightsHack.HookRuntime/README.md)。
- [范围清单](D:/NightsHack/work/world-investigation/hook-scope.json) → [生成器](D:/NightsHack/work/Generate-WorldHookCatalogs.ps1) → 各源码目录 Catalog.json。
- [逐方法覆盖审计](D:/NightsHack/work/world-investigation/hook-coverage-audit.json)：候选和排除均含归属、完整签名、RVA、原因。
- [托管/静态检查](D:/NightsHack/tests/NightsHack.WorldHooks.Checks/Program.cs)。
- 原始结构、公式和调用证据：[world-investigation.md](D:/NightsHack/outputs/world-investigation.md)；Player 档案：[REFERENCE.md](D:/NightsHack/outputs/REFERENCE.md)。

未验证：真实插件加载、原生 patch 数量与命中、全部 interop 投影/字段可读性、复杂 ref/out 参数桥接、帧开销、原生清理行为、存档往返，以及未来物品命令效果。元数据扩展部分未全部达到上轮 202 条反汇编记录的调查深度。

<!-- GENERATED CATALOG -->

## 生成清单统计

| 插件 | 字段 schema | 字段数 | 方法候选 | 明确排除 | 静态候选 |
|---|---:|---:|---:|---:|---:|
| PlayerHook | 32 | 338 | 324 | 46 | 0 |
| WorldHook | 34 | 348 | 350 | 453 | 12 |
| ItemHook | 22 | 203 | 220 | 248 | 11 |
| GameRuntimeHook | 15 | 186 | 173 | 136 | 12 |

字段 schema 可包含只用于读取的支撑类型；没有同名方法不代表没有字段 schema。字段条目按各插件分别计数。

### 保留于 PlayerHook 的重复入口

- `Nivalis.PlayerObjectHolder.StoreEntity(Nivalis.InventorySystem.IItemContainer)`，`0x2DCC3F0`。
- `Nivalis.InventorySystem.PlayerInventory.AddItem(Nivalis.InventorySystem.ItemType, System.Int32)`，`0x2F0BFF0`。
- `Nivalis.InventorySystem.PlayerInventory.AddAllFurniture()`，`0x2F0C050`。
- `Nivalis.InventorySystem.PlayerInventory.ClearItems()`，`0x2F0C440`。

## PlayerHook 候选方法

| 完整方法签名 | 返回类型 | RVA | 静态 | 采样 | 跳过 After 实例读取 |
|---|---|---|---|---|---|
| `Nivalis.PlayerInputManagerSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x2F0A6A0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManagerSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x2F0A6F0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2F05770` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InitializeExternal()` | `System.Void` | `0x2F05870` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x2F058E0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2F05960` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InitializeListeners()` | `System.Collections.IEnumerator` | `0x2F05A00` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.HasSteamInputGamepad()` | `System.Boolean` | `0x2F05A60` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InputDeviceChanged(UnityEngine.InputSystem.InputDevice, UnityEngine.InputSystem.InputDeviceChange)` | `System.Void` | `0x2F05C50` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.UnpairedDeviceUsed(UnityEngine.InputSystem.InputControl, UnityEngine.InputSystem.LowLevel.InputEventPtr)` | `System.Void` | `0x2F05DD0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InputChanged(UnityEngine.InputSystem.Users.InputUser, UnityEngine.InputSystem.Users.InputUserChange, UnityEngine.InputSystem.InputDevice)` | `System.Void` | `0x2F05FD0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.TabRightListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F05FF0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.TabLeftListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06010` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.OnDestroyInternal()` | `System.Void` | `0x2F06030` | 否 | 否 | 是 |
| `Nivalis.PlayerInputManager.JournalShortcutListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06C30` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.ShowMoreShortcutListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06CF0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.ShoppingListShortcutListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06DB0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.InventoryShortcutListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06E70` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.MapToggleListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06F30` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.AnyKeyListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F06FF0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.ScanListener(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F07010` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.GetBindingIndexForControlScheme(UnityEngine.InputSystem.InputAction)` | `System.Int32` | `0x2F07030` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.GetActionForActionReference(UnityEngine.InputSystem.InputActionReference)` | `UnityEngine.InputSystem.InputAction` | `0x2F07210` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.SwitchControlScheme(UnityEngine.InputSystem.InputDevice)` | `System.Void` | `0x2F072B0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.SwitchMenuActionMap(System.Boolean)` | `System.Void` | `0x2F078F0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.SwitchActionMap(UnityEngine.InputSystem.InputActionMap)` | `System.Void` | `0x2F07980` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.SwitchDevice()` | `System.Void` | `0x2F079F0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.IsBindingComposite(UnityEngine.InputSystem.InputActionReference)` | `System.Boolean` | `0x2F07A70` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.GetBindingDisplay(UnityEngine.InputSystem.InputActionReference, System.Int32, System.Boolean)` | `Nivalis.DeviceDisplayConfigurator+DisplayData` | `0x2F07C30` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.StartRebind(UnityEngine.InputSystem.InputActionReference, System.Int32)` | `System.Void` | `0x2F080F0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.ResetRebind(UnityEngine.InputSystem.InputActionReference, System.Int32)` | `System.Void` | `0x2F08150` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.DoRebind(UnityEngine.InputSystem.InputAction, System.Int32)` | `System.Void` | `0x2F088C0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.ConfirmRebind(UnityEngine.InputSystem.InputAction, System.Int32, System.String)` | `System.Void` | `0x2F091F0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.LoadBindings(Nivalis.PlayerInputManagerSave)` | `System.Void` | `0x2F0A2B0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.RequestSave()` | `System.Void` | `0x2F0A300` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.SaveBindings(Nivalis.PlayerInputManagerSave)` | `System.Void` | `0x2F0A390` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.IsSteamInputGamepad(UnityEngine.InputSystem.Gamepad)` | `System.Boolean` | `0x2F0A3E0` | 否 | 否 | 否 |
| `Nivalis.PlayerInputManager.CancelRebind()` | `System.Void` | `0x2F0A500` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.Awake()` | `System.Void` | `0x9B1690` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.Start()` | `System.Void` | `0x9B1820` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.ManualUpdate()` | `System.Void` | `0x9B1990` | 否 | 是 | 否 |
| `Nivalis.PlacementSystem.UpdateHeldObjectPlacementPosition(UnityEngine.Ray)` | `System.Boolean` | `0x9B1F60` | 否 | 是 | 否 |
| `Nivalis.PlacementSystem.OnEnable()` | `System.Void` | `0x9B35B0` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.OnDisable()` | `System.Void` | `0x9B37C0` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.PlayerCaught()` | `System.Void` | `0x9B39A0` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.OnCurfewStart()` | `System.Void` | `0x9B3D50` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.DoPlacement(System.Boolean, UnityEngine.Vector3)` | `System.Void` | `0x9B3EE0` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.OnPickUp(Nivalis.HoldableEntity)` | `System.Void` | `0x9B4220` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.OnStore(Nivalis.HoldableEntity)` | `System.Void` | `0x9B4330` | 否 | 否 | 否 |
| `Nivalis.PlacementSystem.ReleasePlayerLocks()` | `System.Void` | `0x9B4440` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.PositionInFrontOfPlayer(Nivalis.HoldableEntity)` | `UnityEngine.Vector3` | `0x2DCA650` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.Awake()` | `System.Void` | `0x2DCA8E0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.Start()` | `System.Void` | `0x2DCA9A0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.ManualUpdate()` | `System.Void` | `0x2DCAA90` | 否 | 是 | 否 |
| `Nivalis.PlayerObjectHolder.UpdateHeldObjectPosition()` | `System.Void` | `0x2DCADE0` | 否 | 是 | 否 |
| `Nivalis.PlayerObjectHolder.UpdateHeldObjectRotation()` | `System.Void` | `0x2DCB040` | 否 | 是 | 否 |
| `Nivalis.PlayerObjectHolder.TryPickupObjectInFront()` | `System.Void` | `0x2DCB300` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.CheckForObjectInFront()` | `System.Void` | `0x2DCB600` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.HoldObject(Nivalis.HoldableEntity)` | `System.Boolean` | `0x2DCBA80` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.ReleaseObject()` | `System.Void` | `0x2DCBDA0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.StoreEntity(Nivalis.InventorySystem.IItemContainer)` | `System.Boolean` | `0x2DCC3F0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.SetHeldObj(Nivalis.HoldableEntity)` | `System.Void` | `0x2DCC6B0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.OnHeldObjDestroyed()` | `System.Void` | `0x2DCCA20` | 否 | 否 | 是 |
| `Nivalis.PlayerObjectHolder.RotateHeldObject(System.Single, UnityEngine.Vector3)` | `System.Void` | `0x2DCCAD0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.GrabPlayerLocks()` | `System.Void` | `0x2DCCBA0` | 否 | 否 | 否 |
| `Nivalis.PlayerObjectHolder.ReleasePlayerLocks()` | `System.Void` | `0x2DCCC40` | 否 | 否 | 否 |
| `Nivalis.FocusRaycaster.Awake()` | `System.Void` | `0x3031E40` | 否 | 否 | 否 |
| `Nivalis.FocusRaycaster.UpdateFocus()` | `System.Void` | `0x3031EB0` | 否 | 是 | 否 |
| `Nivalis.FocusRaycaster.ClearFocus()` | `System.Void` | `0x3032350` | 否 | 否 | 否 |
| `Nivalis.FocusRaycaster.CheckClearFocus(System.Boolean)` | `System.Void` | `0x3032470` | 否 | 否 | 否 |
| `Nivalis.FocusRaycaster.FindInteractable()` | `Nivalis.IInteractable` | `0x3032500` | 否 | 是 | 否 |
| `Nivalis.FocusRaycaster.FindAgent()` | `Nivalis.GhostSystem.Ai.Agent` | `0x3032730` | 否 | 是 | 否 |
| `Nivalis.FocusRaycaster.FindHoldableEntity(System.Boolean)` | `Nivalis.HoldableEntity` | `0x3032980` | 否 | 是 | 否 |
| `Nivalis.PlayerManager.PlayerVisibility(UnityEngine.Vector3)` | `System.Single` | `0x2F0D810` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2F0D900` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x2F0DDF0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2F0DE70` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.Update()` | `System.Void` | `0x2F0DF40` | 否 | 是 | 否 |
| `Nivalis.PlayerManager.OnDisable()` | `System.Void` | `0x2F0DF70` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.GetPlayer(System.Int32)` | `Nivalis.PlayerManager+Player` | `0x2F0DF90` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.GetPlayer(UnityEngine.GameObject)` | `Nivalis.PlayerManager+Player` | `0x2F0DFA0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager.DEV_SetStat(Nivalis.PlayerStat, System.Single)` | `System.Void` | `0x2F0E150` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+PlayerSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x2D086A0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+PlayerSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x2D08990` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+PlayerSave.Clear()` | `System.Void` | `0x2D08D70` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.MakePreparationStep(Nivalis.PlayerManager+MealPreparationProcessingType)` | `System.Void` | `0x2D01CA0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.CheckOrderIsDone()` | `System.Void` | `0x2D01DD0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.TakeOrder(Nivalis.GhostSystem.CustomerLoop.Order)` | `System.Void` | `0x2D01E90` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetOwnedProperties(System.Collections.Generic.List`1<Nivalis.GhostSystem.CustomerLoop.BaseProperty>)` | `System.Void` | `0x2D02BB0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.IsOwningProperty(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Boolean` | `0x2D02DF0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetOwnedVenues(System.Collections.Generic.List`1<Nivalis.GhostSystem.CustomerLoop.Venue>)` | `System.Void` | `0x2D031B0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.IsOwningVenue(Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Boolean` | `0x2D03460` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetNumberOfOwnedVenues()` | `System.Int32` | `0x2D03840` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.Initialize(System.Int32, Nivalis.PlayerManager+PlayerSave)` | `System.Void` | `0x2D03AB0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.OnDayUpdate()` | `System.Void` | `0x2D04690` | 否 | 是 | 否 |
| `Nivalis.PlayerManager+Player.OnCurfewStart()` | `System.Void` | `0x2D04BE0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.PlayerEntersVenueListener(Nivalis.Locale.VenueArea, System.Boolean)` | `System.Void` | `0x2D04C50` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.PlayerEntersApartmentListener(Nivalis.Apartment.ApartmentController, System.Boolean)` | `System.Void` | `0x2D04EC0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.PlayerEntersGreenhouseListener(Nivalis.GreenhouseArea, System.Boolean)` | `System.Void` | `0x2D05130` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.DeInitialize()` | `System.Void` | `0x2D053A0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.CreateCharacter()` | `System.Void` | `0x2D05B10` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.StartDialogue()` | `System.Void` | `0x2D05C30` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.UpdateRank()` | `System.Void` | `0x2D05CA0` | 否 | 是 | 否 |
| `Nivalis.PlayerManager+Player.AddOwnedProperty(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Void` | `0x2D060D0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.RemoveOwnedProperty(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Void` | `0x2D061E0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.DoesOwnProperty(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Boolean` | `0x2D062D0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetPropertyOwnershipState(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `Nivalis.GhostSystem.CustomerLoop.OwnershipType` | `0x2D06380` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetOwnedInventories(System.Collections.Generic.List`1<Nivalis.InventorySystem.InventoryData>)` | `System.Void` | `0x2D06430` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.TryMakePurchase(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemType, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2D066E0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemType, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2D06930` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemStack, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2D06C70` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemStack+BasicTemp&, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2D06EB0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.PayRent(Nivalis.Player.RentReceipt&, System.Boolean)` | `System.Void` | `0x2D070C0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.AddExperience(Nivalis.SkillSystem.SkillDefinition, System.Single)` | `System.Void` | `0x2D07130` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetDayBalance(System.Int32&, System.Int32&, System.Int32)` | `System.Void` | `0x2D071D0` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetTotalBalance(System.Int32&, System.Int32&)` | `System.Void` | `0x2D07750` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.GetIngredientsUsedInOwnedVenuesMenu(System.Collections.Generic.HashSet`1<Nivalis.InventorySystem.ItemType>)` | `System.Void` | `0x2D07C10` | 否 | 否 | 否 |
| `Nivalis.PlayerManager+Player.EatVenueMeal(Nivalis.GhostSystem.CustomerLoop.MealGhost, Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Void` | `0x2D07F30` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.Awake()` | `System.Void` | `0x9BC920` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.OnEnable()` | `System.Void` | `0x9BCE50` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.OnDisable()` | `System.Void` | `0x9BCED0` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.CameraZoomCooldown()` | `System.Void` | `0x9BD090` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.UpdateCameraZooming()` | `System.Void` | `0x9BD0A0` | 否 | 是 | 否 |
| `Nivalis.PlayerCameraController.ManualUpdate()` | `System.Void` | `0x9BD6D0` | 否 | 是 | 否 |
| `Nivalis.PlayerCameraController.OnDestroy()` | `System.Void` | `0x9BD810` | 否 | 否 | 是 |
| `Nivalis.PlayerCameraController.SetDepthOfField(System.Boolean, System.Single, System.Single)` | `System.Void` | `0x9BDA60` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.ResetFollowTarget()` | `System.Void` | `0x9BE060` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.OnSceneLoad()` | `System.Void` | `0x9BE140` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.UpdatePostProcesVolumes()` | `System.Void` | `0x9BE2A0` | 否 | 是 | 否 |
| `Nivalis.PlayerCameraController.OnDialogueStart(Nivalis.Dialogue.DialogueEventArgs)` | `System.Void` | `0x9BE3C0` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.OnDialogueEnd(Nivalis.Dialogue.DialogueEventArgs)` | `System.Void` | `0x9BE640` | 否 | 否 | 否 |
| `Nivalis.PlayerCameraController.UpdateFocusTarget()` | `System.Void` | `0x9BE660` | 否 | 是 | 否 |
| `Nivalis.PlayerCameraController.SetFocusTarget(UnityEngine.Transform, UnityEngine.Transform, System.Boolean)` | `System.Void` | `0x9BE9A0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.set_DrivenBoat(Nivalis.Boat.BoatController)` | `System.Void` | `0x9BFAB0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.set_ViewId(System.String)` | `System.Void` | `0x9C0250` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.SceneLoadCompletedListener()` | `System.Void` | `0x9C0290` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.Initialize()` | `System.Void` | `0x9C0370` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.Update()` | `System.Void` | `0x9C0710` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacter.ResetPlayerToPosition(Nivalis.PositionRotation)` | `System.Collections.IEnumerator` | `0x9C0AC0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.OnDestroy()` | `System.Void` | `0x9C0C70` | 否 | 否 | 是 |
| `Nivalis.PlayerCharacter.OnPoolingPostDestroy()` | `System.Void` | `0x9C0D70` | 否 | 否 | 是 |
| `Nivalis.PlayerCharacter.CreateGhostFromView(Nivalis.GhostSystem.IGhostRegistry)` | `Nivalis.GhostSystem.Ghost` | `0x9C0EB0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.CreateGhostBaseFromView()` | `Nivalis.GhostSystem.Ghost` | `0x9C0F00` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.FillGhostBaseFromViewState(Nivalis.GhostSystem.Ghost, Nivalis.GhostSystem.IGhostRegistry)` | `System.Boolean` | `0x9C1070` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.SnapToGhost()` | `System.Void` | `0x9C1300` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacter.GetSpotId(Nivalis.AISpot)` | `System.Int32` | `0x9C1420` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacter.GetSpot(System.Int32)` | `Nivalis.AISpot` | `0x9C1860` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.SaveInternal(Nivalis.ISaveWriter)` | `System.Void` | `0x2EF7140` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.LoadInternal(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x2EF7250` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.UpdateOrder()` | `System.Void` | `0x2EF72D0` | 否 | 是 | 否 |
| `Nivalis.PlayerGhost.ChooseMeal()` | `System.Void` | `0x2EF7340` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.SetOrder(Nivalis.GhostSystem.CustomerLoop.VenueOrderReference)` | `System.Void` | `0x2EF74F0` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.ClearOrder()` | `System.Void` | `0x2EF7550` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.SitDown(Nivalis.GhostSystem.CustomerLoop.ChairGhost, Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Void` | `0x2EF7680` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.GetUpFromChair()` | `System.Void` | `0x2EF79E0` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.ReleaseSavingLock()` | `System.Void` | `0x2EF7BA0` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.PayForOrder()` | `System.Void` | `0x2EF7CD0` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.ReserveChair()` | `System.Void` | `0x2EF8090` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.FreeChair()` | `System.Void` | `0x2EF83C0` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.InterruptReservation(Nivalis.GhostSystem.IAgentReservable)` | `System.Void` | `0x2EF8750` | 否 | 否 | 否 |
| `Nivalis.PlayerGhost.OnMealChosen(System.Collections.Generic.List`1<Nivalis.Locale.MealMenuItem>)` | `System.Void` | `0x2EF8960` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.ToggleNoClip()` | `System.Void` | `0x9C18A0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.set_State(Nivalis.PlayerCharacterController+ControllerState)` | `System.Void` | `0x9C18C0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.set_NavMesh(Nivalis.PlayerNavMesh)` | `System.Void` | `0x9C1F50` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.SetPlayerSpeed(System.Single)` | `System.Void` | `0x9C2110` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.DevTeleportToPlayer()` | `System.Void` | `0x9C2130` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.DevUnlockBoat()` | `System.Void` | `0x9C2580` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.Awake()` | `System.Void` | `0x9C2760` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.OnDisable()` | `System.Void` | `0x9C2DF0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.OnEnable()` | `System.Void` | `0x9C2F30` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.EnableDialogueCamera(UnityEngine.Transform, System.Single, UnityEngine.Transform)` | `System.Void` | `0x9C2FD0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.TryHideHands()` | `System.Void` | `0x9C4030` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.DialogueCameraCoroutine()` | `System.Collections.IEnumerator` | `0x9C4140` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.ToggleCameraSmoothingMaxSpeed()` | `System.Void` | `0x9C41A0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.ToggleCameraSmoothing()` | `System.Void` | `0x9C44C0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.RotateCamera(System.Single)` | `System.Void` | `0x9C4840` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.OnGUI()` | `System.Void` | `0x9C4870` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.OnDrawGizmosSelected()` | `System.Void` | `0x9C5030` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.ManualUpdate(Nivalis.Boat.BoatController)` | `System.Void` | `0x9C5220` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.UpdateCameraLook()` | `System.Void` | `0x9C5F00` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.UpdateMovement(System.Single, UnityEngine.Vector3)` | `System.Void` | `0x9C5FA0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.Move(UnityEngine.Vector3)` | `System.Void` | `0x9C66A0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.GetNoclipVerticalInput()` | `System.Single` | `0x9C6E30` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.MoveToGround()` | `System.Void` | `0x9C6F10` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.SnapToGround()` | `System.Void` | `0x9C7040` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.GetGroundDistance()` | `System.Single` | `0x9C7580` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.SnapToLocalPositionOnNavMesh()` | `System.Void` | `0x9C7930` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.UpdateLocalPositionOnNavMesh()` | `System.Void` | `0x9C7B30` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.HeadBob(System.Single, System.Single)` | `System.Void` | `0x9C7D40` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.ResetHeadBob()` | `System.Void` | `0x9C8190` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.HeadBobEnable()` | `System.Void` | `0x9C8280` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.UpdateRotationOffset(UnityEngine.Vector2)` | `System.Void` | `0x9C82A0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.UpdatePositionOffset(UnityEngine.Vector3)` | `System.Void` | `0x9C82D0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.ResetOffset()` | `System.Void` | `0x9C8330` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.UpdateSitting()` | `System.Void` | `0x9C8430` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.CheckPlayerPlacementValid(UnityEngine.Vector3, UnityEngine.Vector3&)` | `System.Boolean` | `0x9C86E0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.RaycastCheckGround(UnityEngine.Vector3)` | `System.Boolean` | `0x9C89B0` | 否 | 是 | 否 |
| `Nivalis.PlayerCharacterController.TeleportPlayer(UnityEngine.Vector3, UnityEngine.Quaternion, System.Boolean)` | `System.Void` | `0x9C8EC0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.TeleportPlayer(UnityEngine.Transform, System.Boolean)` | `System.Void` | `0x9C9270` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.DisableSprint(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x9C93B0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.DisableMovement(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x9C93F0` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.LockCamera(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x9C9430` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.StopMovement()` | `System.Void` | `0x9C9470` | 否 | 否 | 否 |
| `Nivalis.PlayerCharacterController.ChangeCameraFov(System.Single)` | `System.Void` | `0x9C94C0` | 否 | 否 | 否 |
| `Nivalis.PlayerEnvironmentTracker.Update()` | `System.Void` | `0x2EF1CE0` | 否 | 是 | 否 |
| `Nivalis.PlayerEnvironmentTracker.IsInsideProperty()` | `System.Boolean` | `0x2EF1D00` | 否 | 否 | 否 |
| `Nivalis.PlayerEnvironmentTracker.IsUnderRoof()` | `System.Boolean` | `0x2EF1E60` | 否 | 否 | 否 |
| `Nivalis.PlayerEnvironmentTracker.ApplyVolumes(System.Single&)` | `System.Boolean` | `0x2EF2020` | 否 | 否 | 否 |
| `Nivalis.PlayerEnvironmentTracker.UpdateInterior()` | `System.Void` | `0x2EF2210` | 否 | 是 | 否 |
| `Nivalis.PlayerEnvironmentTracker.UpdateClosestVenue()` | `System.Void` | `0x2EF2630` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.set_RootMotion(System.Boolean)` | `System.Void` | `0x2EF8B20` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.Awake()` | `System.Void` | `0x2EF8C00` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.Start()` | `System.Void` | `0x2EF8DD0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.ManualUpdate()` | `System.Void` | `0x2EF9210` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.OnDestroy()` | `System.Void` | `0x2EF9400` | 否 | 否 | 是 |
| `Nivalis.PlayerHandsAnimator.Init()` | `System.Void` | `0x2EF99C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.AnimatorStartInteract(UnityEngine.AnimationEvent)` | `System.Void` | `0x2EF9A30` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.AnimatorStopInteract(UnityEngine.AnimationEvent)` | `System.Void` | `0x2EF9EF0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.Hide(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x2EFA0D0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.Eat(Nivalis.GhostSystem.CustomerLoop.MealView, System.Action)` | `System.Void` | `0x2EFA110` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetRigParent(UnityEngine.Transform)` | `System.Void` | `0x2EFA1C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateIK()` | `System.Void` | `0x2EFAE80` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.LimitFreelook()` | `System.Void` | `0x2EFAEC0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.EatingSequence(Nivalis.GhostSystem.CustomerLoop.MealView, System.Action)` | `System.Collections.IEnumerator` | `0x2EFB0F0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateFreelookCamera()` | `System.Void` | `0x2EFB1A0` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateRig()` | `System.Void` | `0x2EFB8B0` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateRootMotion()` | `System.Void` | `0x2EFC410` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.ResetRootMotion()` | `System.Void` | `0x2EFC8C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetCameraTargetOverride(System.Boolean, Nivalis.PlayerHandsAnimator+EulerPose, UnityEngine.Pose)` | `System.Void` | `0x2EFCAB0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateCamera()` | `System.Void` | `0x2EFCDD0` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.LockPlayer()` | `System.Void` | `0x2EFDE20` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UnlockPlayer()` | `System.Void` | `0x2EFDF80` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.LockCamera()` | `System.Void` | `0x2EFE0A0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UnlockCamera()` | `System.Void` | `0x2EFE260` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateSpeed()` | `System.Void` | `0x2EFE4C0` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdatePlacement()` | `System.Void` | `0x2EFE750` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateTray()` | `System.Void` | `0x2EFE950` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateSitting()` | `System.Void` | `0x2EFED60` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.SitDownSequence()` | `System.Collections.IEnumerator` | `0x2EFF0A0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StandUpSequence()` | `System.Collections.IEnumerator` | `0x2EFF100` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.OnItemPlaced(Nivalis.ItemPlacedArguments)` | `System.Void` | `0x2EFF160` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlaySweepAnimation(UnityEngine.Pose, System.Action)` | `System.Void` | `0x2EFF260` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SweepSequence(UnityEngine.Pose, System.Action)` | `System.Collections.IEnumerator` | `0x2EFF310` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayIngredientProcessorAnimation(Nivalis.AISpot, Nivalis.CraftingSystem.IngredientProcessingType, System.Action)` | `System.Void` | `0x2EFF3B0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayFoodProcessorAnimation(Nivalis.AISpot, System.Boolean, System.Action)` | `System.Void` | `0x2EFF7D0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.CookingSequence(Nivalis.AISpot, System.String, System.Single, UnityEngine.Vector3, System.Action)` | `System.Collections.IEnumerator` | `0x2EFF8A0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayPlantAnimation(UnityEngine.Pose, Nivalis.PlayerHandsAnimator+FarmingAnimationPose, System.Action)` | `System.Void` | `0x2EFF990` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlantSequence(Nivalis.PlayerHandsAnimator+FarmingAnimationPose, System.Action)` | `System.Collections.IEnumerator` | `0x2EFFDA0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayHarvestAnimation(UnityEngine.Pose, Nivalis.PlayerHandsAnimator+FarmingAnimationPose, System.Action, Nivalis.InventorySystem.ItemType)` | `System.Void` | `0x2EFFE30` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.HarvestSequence(UnityEngine.Pose, Nivalis.PlayerHandsAnimator+FarmingAnimationPose, System.Action, Nivalis.InventorySystem.ItemType)` | `System.Collections.IEnumerator` | `0x2F00270` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlantPullSequence(Nivalis.InventorySystem.ItemType, UnityEngine.Pose)` | `System.Collections.IEnumerator` | `0x2F00320` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.OnVisibleValueChanged()` | `System.Void` | `0x2F003C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlaySleepAnimation(Nivalis.IBed, System.Boolean)` | `System.Void` | `0x2F004C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayAwakeAnimation()` | `System.Void` | `0x2F00560` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SleepSequence(Nivalis.IBed, System.Boolean)` | `System.Collections.IEnumerator` | `0x2F005A0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetFishing(System.Boolean)` | `System.Void` | `0x2F00630` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayFishingRodBigSwing()` | `System.Void` | `0x2F007D0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.FishingRodBigSwingSequence()` | `System.Collections.IEnumerator` | `0x2F00840` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetFishCatch(System.Boolean)` | `System.Void` | `0x2F008A0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetLeftHandIK(UnityEngine.Transform, System.Single, System.Single)` | `System.Void` | `0x2F00960` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.ClearLeftHandIK()` | `System.Void` | `0x2F009D0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetRightHandIK(UnityEngine.Transform, System.Single, System.Single)` | `System.Void` | `0x2F00A40` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.ClearRightHandIK()` | `System.Void` | `0x2F00AB0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.OnInteract(Nivalis.IInteractable)` | `System.Void` | `0x2F00B20` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayMealConsumeSFX(System.Boolean, System.Boolean)` | `System.Void` | `0x2F00CA0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.PlayInteraction(Nivalis.InteractionAnimation)` | `System.Void` | `0x2F00CF0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StartHoldingMeal(Nivalis.GhostSystem.CustomerLoop.MealView)` | `System.Void` | `0x2F00DC0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StartHoldingMealSequence(Nivalis.GhostSystem.CustomerLoop.MealView)` | `System.Collections.IEnumerator` | `0x2F00E90` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StopHoldingMeal(Nivalis.GhostSystem.CustomerLoop.MealView, System.Action)` | `System.Void` | `0x2F00F10` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StopHoldingMealSequence(Nivalis.GhostSystem.CustomerLoop.MealView, System.Action)` | `System.Collections.IEnumerator` | `0x2F00FE0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.ToggleNewspaper()` | `System.Void` | `0x2F01090` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.TryToEquipNewspaper(System.Boolean)` | `System.Void` | `0x2F01230` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.EquipNewspaper()` | `UnityEngine.Coroutine` | `0x2F013C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.TryUnequipNewspaper()` | `System.Void` | `0x2F01450` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.NewspaperSequence()` | `System.Collections.IEnumerator` | `0x2F015C0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.StartCameraTilt(System.Single, System.Single)` | `System.Void` | `0x2F01620` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.CameraTiltSequence(System.Single, System.Single)` | `System.Collections.IEnumerator` | `0x2F016B0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.SetDutchAngle(System.Single)` | `System.Void` | `0x2F01730` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.UpdateFootsteps()` | `System.Void` | `0x2F017C0` | 否 | 是 | 否 |
| `Nivalis.PlayerHandsAnimator.GetSurface()` | `System.String` | `0x2F01CF0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.IsInPuddle(UnityEngine.RaycastHit)` | `System.Boolean` | `0x2F02290` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.IsInSnow(UnityEngine.RaycastHit)` | `System.Boolean` | `0x2F02410` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.ReleaseTrayMeals()` | `System.Void` | `0x2F02540` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.<SleepSequence>b__144_0()` | `System.Boolean` | `0x2F02AB0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.<NewspaperSequence>b__164_0()` | `System.Boolean` | `0x2F02AC0` | 否 | 否 | 否 |
| `Nivalis.PlayerHandsAnimator.<NewspaperSequence>b__164_1()` | `System.Boolean` | `0x2F02AE0` | 否 | 否 | 否 |
| `Nivalis.PlayerInteraction.Awake()` | `System.Void` | `0x2F0A9E0` | 否 | 否 | 否 |
| `Nivalis.PlayerInteraction.Start()` | `System.Void` | `0x2F0AC50` | 否 | 否 | 否 |
| `Nivalis.PlayerInteraction.OnDestroy()` | `System.Void` | `0x2F0ADD0` | 否 | 否 | 是 |
| `Nivalis.PlayerInteraction.InteractionActiveChangeListener()` | `System.Void` | `0x2F0B120` | 否 | 否 | 否 |
| `Nivalis.PlayerInteraction.ManualUpdate()` | `System.Void` | `0x2F0B260` | 否 | 是 | 否 |
| `Nivalis.PlayerInteraction.UpdateInteractable()` | `System.Void` | `0x2F0B4A0` | 否 | 是 | 否 |
| `Nivalis.PlayerInteraction.OnInteraction(UnityEngine.InputSystem.InputAction+CallbackContext)` | `System.Void` | `0x2F0B610` | 否 | 否 | 否 |
| `Nivalis.PlayerInteraction.OnInteractableDestroyed(Nivalis.IInteractable)` | `System.Void` | `0x2F0B770` | 否 | 否 | 是 |
| `Nivalis.PlayerInteraction.DisableInteractions(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x2F0B790` | 否 | 否 | 否 |
| `Nivalis.PlayerManagerSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x2F0E3F0` | 否 | 否 | 否 |
| `Nivalis.PlayerManagerSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x2F0E440` | 否 | 否 | 否 |
| `Nivalis.PlayerManagerSave.Clear()` | `System.Void` | `0x2F0E600` | 否 | 否 | 否 |
| `Nivalis.PlayerNavMesh.OnTriggerEnter(UnityEngine.Collider)` | `System.Void` | `0x2F0F090` | 否 | 否 | 否 |
| `Nivalis.PlayerNavMesh.OnTriggerExit(UnityEngine.Collider)` | `System.Void` | `0x2F0F230` | 否 | 否 | 否 |
| `Nivalis.PlayerNavMesh.Snap(UnityEngine.Vector3)` | `UnityEngine.Vector3` | `0x2F0F440` | 否 | 是 | 否 |
| `Nivalis.PlayerNavMesh.IsInsideObstacle(UnityEngine.Vector3)` | `System.Boolean` | `0x2F0FAF0` | 否 | 否 | 否 |
| `Nivalis.PlayerNavMesh.InitMeshData()` | `System.Void` | `0x2F0FCD0` | 否 | 否 | 否 |
| `Nivalis.PlayerState.GetStatValue(Nivalis.PlayerStat)` | `System.Single` | `0x2DCE000` | 否 | 是 | 否 |
| `Nivalis.PlayerState.Update()` | `System.Void` | `0x2DCE250` | 否 | 是 | 否 |
| `Nivalis.PlayerState.SetStat(Nivalis.PlayerStat, System.Single)` | `System.Void` | `0x2DCE4F0` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.InitializeProviderData()` | `System.Void` | `0x2FEB410` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2FEB5F0` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.AddExperience(Nivalis.SkillSystem.SkillDefinition, System.Int32, System.Single)` | `System.Void` | `0x2FEB820` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.GetPlayerSkillExperience(Nivalis.SkillSystem.SkillDefinition)` | `Nivalis.SkillSystem.SkillLevelController+PlayerSkillExperience` | `0x2FEBCF0` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.CreatePacket()` | `Nivalis.ISavePacket` | `0x2FEBE20` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2FEBF70` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController.TryProvide(System.String)` | `UnityEngine.ScriptableObject` | `0x2FEC1B0` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController+SkillLevelsControllerSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x2D3D920` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController+SkillLevelsControllerSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x2D3DCB0` | 否 | 否 | 否 |
| `Nivalis.SkillSystem.SkillLevelController+SkillLevelsControllerSave.Clear()` | `System.Void` | `0x2D3DE20` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.set_Money(System.Int32)` | `System.Void` | `0x2F0B9A0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.Awake()` | `System.Void` | `0x2F0BAC0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.OnMoneyEventLockChanged()` | `System.Void` | `0x2F0BE70` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.Start()` | `System.Void` | `0x2F0BF70` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.AddItem(Nivalis.InventorySystem.ItemType, System.Int32)` | `System.Void` | `0x2F0BFF0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.AddAllFurniture()` | `System.Void` | `0x2F0C050` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.ClearItems()` | `System.Void` | `0x2F0C440` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.PlayerInventory.TakeMoney(System.Int32)` | `System.Void` | `0x2F0C460` | 否 | 否 | 否 |

### 排除原因分布

- Shared native RVA; ambiguous entry：29。
- Static entry (instance observer only)：9。
- Generic entry; observe non-generic caller：8。

## WorldHook 候选方法

| 完整方法签名 | 返回类型 | RVA | 静态 | 采样 | 跳过 After 实例读取 |
|---|---|---|---|---|---|
| `Nivalis.GameSceneManager+<LoadAreaRoutine>d__85.MoveNext()` | `System.Boolean` | `0x2D68E50` | 否 | 是 | 是 |
| `Nivalis.GameSceneManager+<WaitForLevelLoadingUnblocked>d__86.MoveNext()` | `System.Boolean` | `0x2D1B1B0` | 否 | 是 | 是 |
| `Nivalis.GameSceneManager.set_ManualCameraUpdate(System.Boolean)` | `System.Void` | `0x3179910` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.set_IsUnloadingGameplay(System.Boolean)` | `System.Void` | `0x3179A90` | 是 | 否 | 是 |
| `Nivalis.GameSceneManager.GetWorldLocation(System.Int32)` | `Nivalis.WorldLocation` | `0x3179EA0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.GetWorldLocation(System.String)` | `Nivalis.WorldLocation` | `0x3179F80` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.set_TimeSinceStart(System.Single)` | `System.Void` | `0x317A220` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.set_IsLoadingExternal(System.Boolean)` | `System.Void` | `0x317A2B0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.IsGameplayScene(System.Int32)` | `System.Boolean` | `0x317A330` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.Awake()` | `System.Void` | `0x317A3A0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.OnDestroyInternal()` | `System.Void` | `0x317AF10` | 否 | 否 | 是 |
| `Nivalis.GameSceneManager.DebugIsValidSaveSlot()` | `System.Boolean` | `0x317B360` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.DebugLoadFromSaveSlot(System.String)` | `System.Collections.IEnumerator` | `0x317B480` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.Start()` | `System.Void` | `0x317B4E0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.LoadArea(System.String, System.String, System.Action, System.Boolean, System.Boolean, System.Boolean, System.Boolean)` | `System.Void` | `0x317B5F0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.LoadAreaRoutine(System.String, System.String, System.Action, System.Boolean, System.Boolean, System.Boolean)` | `System.Collections.IEnumerator` | `0x317B860` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.WaitForLevelLoadingUnblocked()` | `System.Collections.IEnumerator` | `0x317B930` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.UnloadArea(System.String)` | `UnityEngine.AsyncOperation` | `0x317B990` | 否 | 否 | 是 |
| `Nivalis.GameSceneManager.StartGame()` | `System.Void` | `0x317B9F0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.StartLoadedGame(System.Int32, System.Action)` | `System.Void` | `0x317BBB0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.EndGame()` | `System.Void` | `0x317BD50` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.UnloadGameplayTest()` | `System.Void` | `0x317BD90` | 否 | 否 | 是 |
| `Nivalis.GameSceneManager.UnloadGameplay()` | `System.Collections.IEnumerator` | `0x317BE00` | 否 | 否 | 是 |
| `Nivalis.GameSceneManager.LoadSceneObjects(System.Boolean, Nivalis.SaveReader, System.Boolean, System.Boolean)` | `System.Void` | `0x317BE60` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.FinalizeManagerInitialization(System.Boolean)` | `System.Void` | `0x317BF20` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.EnableCreditsPrewarming()` | `System.Void` | `0x317BF50` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.DisableCreditsPrewarming()` | `System.Void` | `0x317BFB0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.ToggleCreditsUI()` | `System.Void` | `0x317C010` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.LoadCredits()` | `System.Void` | `0x317C2E0` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.UpdateCamera()` | `System.Void` | `0x317C420` | 否 | 否 | 否 |
| `Nivalis.GameSceneManager.GetReadableName(System.Int32)` | `System.String` | `0x317C4A0` | 是 | 否 | 否 |
| `Nivalis.TransitionManager+<TransitionRoutine>d__23.MoveNext()` | `System.Boolean` | `0x2D909B0` | 否 | 是 | 是 |
| `Nivalis.Greenhouse.set_OwnerInventory(Nivalis.InventorySystem.IInventory)` | `System.Void` | `0x306EFB0` | 否 | 否 | 否 |
| `Nivalis.Greenhouse.set_OwnershipType(Nivalis.GhostSystem.CustomerLoop.OwnershipType)` | `System.Void` | `0x306F160` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.Provide(System.Type, System.String)` | `UnityEngine.ScriptableObject` | `0x3078DC0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.TryProvide(System.String)` | `UnityEngine.ScriptableObject` | `0x3078FD0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.InitializeProviderData()` | `System.Void` | `0x3079100` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x3079480` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.IsSkillLevelUnlocked(System.Int32)` | `System.Boolean` | `0x3079AF0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.IsConditionsSetupUnlocked()` | `System.Boolean` | `0x3079BC0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.IsFarmingUpgradesUnlocked()` | `System.Boolean` | `0x3079C60` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.InitializeExternal()` | `System.Void` | `0x3079D00` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.Start()` | `System.Void` | `0x307A4C0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.Update()` | `System.Void` | `0x307A530` | 否 | 是 | 否 |
| `Nivalis.GreenhouseManager.OnDestroyInternal()` | `System.Void` | `0x307A710` | 否 | 否 | 是 |
| `Nivalis.GreenhouseManager.RegisterHarvest(Nivalis.InventorySystem.ItemType)` | `System.Void` | `0x307A8F0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.WasPlantHarvested(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x307A9C0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.GetArea(Nivalis.Greenhouse)` | `Nivalis.GreenhouseAreaGhost` | `0x307AA20` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.GetPlantModules(Nivalis.InventorySystem.ItemType)` | `Nivalis.InventorySystem.ItemType[]` | `0x307AAF0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.GetPlantModules(Nivalis.InventorySystem.ItemType[])` | `Nivalis.InventorySystem.ItemType[]` | `0x307ACA0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x307AEB0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x307AFE0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.Buy(Nivalis.GreenhouseAreaGhost, Nivalis.PlayerManager+Player)` | `System.Void` | `0x307B0E0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.Sell(Nivalis.GreenhouseAreaGhost, Nivalis.PlayerManager+Player)` | `System.Void` | `0x307B360` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.StartRenting(Nivalis.GreenhouseAreaGhost, Nivalis.PlayerManager+Player)` | `System.Void` | `0x307B5D0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.StopRenting(Nivalis.GreenhouseAreaGhost, Nivalis.PlayerManager+Player)` | `System.Void` | `0x307B790` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.OnObjectStore(Nivalis.HoldableEntity)` | `System.Void` | `0x307B950` | 否 | 否 | 否 |
| `Nivalis.GreenhouseManager.SpeedUpFarming()` | `System.Void` | `0x307BAE0` | 否 | 否 | 否 |
| `Nivalis.PortalKey.get_SceneIndex()` | `System.Int32` | `0x2DD8050` | 否 | 是 | 否 |
| `Nivalis.TravelManager+<TeleportPlayer>d__15.MoveNext()` | `System.Boolean` | `0x2D920F0` | 否 | 是 | 是 |
| `Nivalis.TravelManager.RequestDefaultTravel()` | `System.Void` | `0x2E631E0` | 否 | 否 | 否 |
| `Nivalis.TravelManager.RequestDefaultTravelSequence()` | `System.Collections.IEnumerator` | `0x2E63250` | 否 | 否 | 否 |
| `Nivalis.TravelManager.RequestTravel(Nivalis.PortalKey, System.Boolean, System.Boolean)` | `System.Void` | `0x2E632B0` | 否 | 否 | 否 |
| `Nivalis.TravelManager.RequestTravel(Nivalis.PortalKey, System.Boolean, System.Boolean, Nivalis.Boat.BoatGhost)` | `System.Void` | `0x2E632D0` | 否 | 否 | 否 |
| `Nivalis.TravelManager.HandleBoatsOnTravel(Nivalis.Boat.BoatGhost)` | `System.Void` | `0x2E63890` | 否 | 否 | 否 |
| `Nivalis.TravelManager.TeleportPlayer(Nivalis.TravelPortal, System.Boolean, Nivalis.Boat.BoatGhost)` | `System.Collections.IEnumerator` | `0x2E63C50` | 否 | 否 | 否 |
| `Nivalis.TravelManager.GetTravelPortal(Nivalis.PortalKey)` | `Nivalis.TravelPortal` | `0x2E63CF0` | 否 | 否 | 否 |
| `Nivalis.TravelManager.RegisterPortal(Nivalis.TravelPortal)` | `System.Void` | `0x2E63DC0` | 否 | 否 | 否 |
| `Nivalis.TravelManager.DeregisterPortal(Nivalis.TravelPortal)` | `System.Void` | `0x2E63E80` | 否 | 否 | 是 |
| `Nivalis.TravelManager.get_CanTravel()` | `System.Boolean` | `0x2E63F90` | 否 | 是 | 否 |
| `Nivalis.TravelManager.FindShortestPath(Nivalis.WorldLocation, Nivalis.WorldLocation, System.Boolean)` | `System.Collections.Generic.List`1<Nivalis.WorldLocation>` | `0x2E63FF0` | 否 | 是 | 否 |
| `Nivalis.TravelManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2E64910` | 否 | 否 | 否 |
| `Nivalis.TravelManager.InitializeExternal()` | `System.Void` | `0x2E64A00` | 否 | 否 | 否 |
| `Nivalis.TravelManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x2E64C60` | 否 | 否 | 否 |
| `Nivalis.TravelManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2E64CE0` | 否 | 否 | 否 |
| `Nivalis.TravelPortal.Awake()` | `System.Void` | `0x2E652A0` | 否 | 否 | 否 |
| `Nivalis.TravelPortal.OnDestroy()` | `System.Void` | `0x2E65400` | 否 | 否 | 是 |
| `Nivalis.TravelPortal.ForceTeleport()` | `System.Void` | `0x2E65510` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.PathfindClosestPathToLocation(Nivalis.WorldLocation, Nivalis.WorldLocation)` | `System.Void` | `0x2E67630` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.InitializeProviderData()` | `System.Void` | `0x2E67900` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2E67A30` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.InitializeExternal()` | `System.Void` | `0x2E67FA0` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x2E68160` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2E68260` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.VisitLocation(Nivalis.WorldLocation)` | `System.Void` | `0x2E68550` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.IsLocationUnlocked(Nivalis.WorldLocation)` | `System.Boolean` | `0x2E68630` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.IsLocationVisited(Nivalis.WorldLocation)` | `System.Boolean` | `0x2E68690` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.TryProvide(System.String)` | `UnityEngine.ScriptableObject` | `0x2E686F0` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.UnlockLocation(Nivalis.WorldLocation)` | `System.Void` | `0x2E68A20` | 否 | 否 | 否 |
| `Nivalis.TravelUnlockManager.UnlockAll()` | `System.Void` | `0x2E68AE0` | 否 | 否 | 否 |
| `Nivalis.WorldLocation.get_SceneIndex()` | `System.Int32` | `0x2F9DC80` | 否 | 是 | 否 |
| `Nivalis.WorldLocation.get_SceneIsLoaded()` | `System.Boolean` | `0x2F9DD00` | 否 | 是 | 否 |
| `Nivalis.GreenhouseArea.CreateGhostBaseFromView()` | `Nivalis.GhostSystem.Ghost` | `0x306F300` | 否 | 否 | 否 |
| `Nivalis.GreenhouseArea.FillGhostBaseFromViewState(Nivalis.GreenhouseAreaGhost, Nivalis.GhostSystem.IGhostRegistry)` | `System.Boolean` | `0x306F370` | 否 | 否 | 否 |
| `Nivalis.GreenhouseArea.Start()` | `System.Void` | `0x30700D0` | 否 | 否 | 否 |
| `Nivalis.GreenhouseArea.Update()` | `System.Void` | `0x30704E0` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.BuildTimeSceneGhosts.GetEnumerator()` | `System.Collections.Generic.IEnumerator`1<Nivalis.GhostSystem.Ghost>` | `0x3097DF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.InvokeStatePropertyChanged()` | `System.Void` | `0x305CEF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.set_IsViewLess(System.Boolean)` | `System.Void` | `0x305CF40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.set_Bounds(UnityEngine.Bounds)` | `System.Void` | `0x305D020` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.Unlink()` | `System.Void` | `0x305D040` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.Ghost.set_Transform(Nivalis.PositionRotation)` | `System.Void` | `0x305D140` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.set_Position(UnityEngine.Vector3)` | `System.Void` | `0x305D1E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.set_LoadedView(Nivalis.GhostSystem.IGhostView)` | `System.Void` | `0x305D4E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.TryGetLoadedView(Nivalis.GhostSystem.IGhostView&)` | `System.Boolean` | `0x305D680` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.DebugStateString()` | `System.ValueTuple`2<System.String,System.String>[]` | `0x305D8D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x305D990` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.LoadHeader(Nivalis.SaveReader, Nivalis.GhostSystem.Ghost+GhostHeader&)` | `System.Void` | `0x305DBC0` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.Load(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x305DD10` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.Ghost.SetupSavedValues(Nivalis.SavedObject)` | `System.Void` | `0x305DF60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ghost.GetGhostLabelName()` | `System.String` | `0x305E120` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.set_Instance(Nivalis.GhostSystem.GhostManager)` | `System.Void` | `0x305EA10` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.TryGetInstance(Nivalis.GhostSystem.GhostManager&)` | `System.Boolean` | `0x305EB80` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.OnGhostViewChanged(Nivalis.GhostSystem.Ghost, System.String, System.String)` | `System.Void` | `0x305ECC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x305ED60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.OnDestroy()` | `System.Void` | `0x305F100` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.GhostManager.InitializeExternal()` | `System.Void` | `0x305F310` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x305F370` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.RegisterBuildTimeGhosts()` | `System.Void` | `0x305F3F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.ManuallyInitializeSystem()` | `System.Void` | `0x305F570` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.CreateBuildTimeGhostCopies()` | `System.Collections.Generic.Dictionary`2<System.String,Nivalis.GhostSystem.Ghost>` | `0x305F5E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.InitializeBakedGhostRuntimeCopies()` | `System.Void` | `0x305F850` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.RegisterNewFilterView(Nivalis.GhostSystem.GhostFilterViewBase)` | `System.Void` | `0x305F9B0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.UpdatePosition(Nivalis.GhostSystem.Ghost, UnityEngine.Vector3)` | `System.Void` | `0x305FB40` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.GhostManager.LateUpdate()` | `System.Void` | `0x305FF60` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.GhostManager.FlushGhostOperationQueues()` | `System.Void` | `0x305FF70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.DeregisterGhostImmediate(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x30602E0` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.GhostManager.RegisterGhostImmediate(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x30609D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.InstantiateGhostViewIfNeeded(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x3061110` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.DeregisterGhost(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x3061AA0` | 是 | 否 | 是 |
| `Nivalis.GhostSystem.GhostManager.RegisterGhost(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x3061C50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.GetGhostById(System.String)` | `Nivalis.GhostSystem.Ghost` | `0x3061CB0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.GetGhostByViewId(System.String)` | `Nivalis.GhostSystem.Ghost` | `0x3061D80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.TryGetGhostByViewId(System.String, Nivalis.GhostSystem.Ghost&)` | `System.Boolean` | `0x3061E50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.TransferGhostToScene(Nivalis.GhostSystem.Ghost, System.Int32, Nivalis.PositionRotation)` | `System.Void` | `0x3061EB0` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.GenerateGUID()` | `System.String` | `0x30621D0` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.LoadBuildTimeGhostBase(System.String, Nivalis.GhostSystem.Ghost&)` | `System.Boolean` | `0x3062270` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.GhostManager.UpdateGhostViewPresence(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x30623F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.ScenePositionalRegistry.MoveGhost(Nivalis.GhostSystem.Ghost, UnityEngine.Vector2Int)` | `System.Void` | `0x319BD10` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.ScenePositionalRegistry.AddGhost(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x319BFF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.ScenePositionalRegistry.RemoveGhost(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x319C060` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.StaticGhostViewBase.CreateGhostFromView(Nivalis.GhostSystem.IGhostRegistry)` | `Nivalis.GhostSystem.Ghost` | `0x2E0C770` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.StaticGhostViewBase.RegisterSpotsInternal()` | `System.Void` | `0x2E0C7E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.StaticGhostViewBase.GetSpotId(Nivalis.AISpot)` | `System.Int32` | `0x2E0C8A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.StaticGhostViewBase.RegisterSpot(Nivalis.AISpot)` | `System.Int32` | `0x2E0C920` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.StaticGhostViewBase.GetSpot(System.Int32)` | `Nivalis.AISpot` | `0x2E0C990` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.ActivateQuestPoint(System.String, System.Int32, Nivalis.Quest, System.Boolean, System.Boolean)` | `System.Void` | `0x2FA4A50` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.DeactivateQuestPoint(System.String, System.Int32, Nivalis.Quest, System.Boolean, System.Boolean)` | `System.Void` | `0x2FA4B10` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.InvokeOnTargetsChanged()` | `System.Void` | `0x2FA4BD0` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.InitializeExternal()` | `System.Void` | `0x2FA4CC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.CacheAllWorldPoints()` | `System.Void` | `0x2FA4E50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.TriggerQueuedActivations()` | `System.Void` | `0x2FA51C0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.GetWorldObjectGhost(System.String, Nivalis.Dialogue.WorldObjectGhost&)` | `System.Boolean` | `0x2FA53D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.GetWorldPointData(System.String)` | `Nivalis.GhostSystem.WorldPointData` | `0x2FA5440` | 是 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.ActivateQuestPointInternal(System.String, System.Int32, Nivalis.Quest, System.Boolean, System.Boolean)` | `System.Void` | `0x2FA5930` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.WorldPointManager.DeactivateQuestPointInternal(System.String, System.Int32, Nivalis.Quest, System.Boolean, System.Boolean)` | `System.Void` | `0x2FA5ED0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.BaseProperty.StartRenting(Nivalis.IPropertyOwner)` | `System.Void` | `0x32518F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.BaseProperty.StopRenting(Nivalis.IPropertyOwner)` | `System.Void` | `0x3251A90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.BaseProperty.ListItems()` | `System.Void` | `0x3251CE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.BaseProperty.EnableHighLight(Nivalis.InventorySystem.ItemType)` | `System.Void` | `0x3251EC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.BaseProperty.DisableHighLight()` | `System.Void` | `0x3252510` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_IsOpen(BooleanSimple)` | `System.Void` | `0x2EC5B50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_Reviews(Nivalis.GhostSystem.CustomerLoop.RuntimeReviewsList)` | `System.Void` | `0x2EC5F40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_Menu(Nivalis.Locale.MealMenu)` | `System.Void` | `0x2EC5FE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_OwnerInventory(Nivalis.InventorySystem.IInventory)` | `System.Void` | `0x2EC6080` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_OwnershipType(Nivalis.GhostSystem.CustomerLoop.OwnershipType)` | `System.Void` | `0x2EC6150` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.set_CurrentDebt(System.Int32)` | `System.Void` | `0x2EC62C0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.DEV_SetLevel(System.Int32)` | `System.Void` | `0x2EC63C0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.CreateTray(UnityEngine.Vector3, UnityEngine.Quaternion, Nivalis.GhostSystem.GhostReference`1<Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost>)` | `Nivalis.GhostSystem.CustomerLoop.TrayGhost` | `0x2EC6440` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.IsIngredientUsed(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x2EC6750` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetIngredientsDemand()` | `System.Collections.Generic.Dictionary`2<Nivalis.InventorySystem.ItemType,Nivalis.ShoppingListManager+ShoppingListData>` | `0x2EC6860` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetChair(Nivalis.InventorySystem.ItemType+SlotType)` | `Nivalis.InventorySystem.ItemType` | `0x2EC6DE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.StartRenting(Nivalis.IPropertyOwner)` | `System.Void` | `0x2EC6F90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.StopRenting(Nivalis.IPropertyOwner)` | `System.Void` | `0x2EC71D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetRewardData()` | `RewardData` | `0x2EC7500` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetPriceMultiplier()` | `System.Single` | `0x2EC7F20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.TestCongratulation(System.Int32)` | `System.Void` | `0x2EC7FB0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetItemsUsedInVenue(System.Collections.Generic.HashSet`1<Nivalis.InventorySystem.ItemType>&)` | `Nivalis.HashSetPool`1+Handle<Nivalis.InventorySystem.ItemType>` | `0x2EC8330` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.GetVenueState()` | `Nivalis.GhostSystem.CustomerLoop.Venue+VenueState` | `0x2EC8580` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.OnIngredientInventoryChanged(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemTypeCountChange)` | `System.Void` | `0x2EC8640` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.MenuChangeListener(ScriptableObjectCollection+ChangeType, System.Int32)` | `System.Void` | `0x2EC8650` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.FurnitureChangeListener()` | `System.Void` | `0x2EC8670` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ReviewsChangedListener(ScriptableObjectCollection+ChangeType, System.Int32)` | `System.Void` | `0x2EC8680` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ReceiptsChangedListener(Nivalis.GhostSystem.CustomerLoop.Order)` | `System.Void` | `0x2EC8690` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.LevelChangeListener(Nivalis.GhostSystem.CustomerLoop.Venue, System.Boolean)` | `System.Void` | `0x2EC8820` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshHasAllIngredients()` | `System.Void` | `0x2EC8830` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshMenuCount()` | `System.Void` | `0x2EC8C90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshSeatsCount()` | `System.Void` | `0x2EC9110` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshReviews()` | `System.Void` | `0x2EC95A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshCustomersServed()` | `System.Void` | `0x2EC99F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.ArticyRefreshLevel()` | `System.Void` | `0x2EC9B20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.Venue.AddVenueDebugOnly()` | `System.Void` | `0x2EC9D50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.GetReviewScore()` | `System.Single` | `0x2ECF440` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.UpdatePopularity()` | `System.Void` | `0x2ECF780` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.ContainsFurniture(Nivalis.GhostSystem.GhostReference`1<Nivalis.GhostSystem.Ghost>)` | `System.Boolean` | `0x2ED07A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.FurnitureTaken(UnityEngine.Vector3)` | `System.Boolean` | `0x2ED0A20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasValidWashingBasin()` | `System.Boolean` | `0x2ED0C50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.GetValidWashingBasin(Nivalis.GhostSystem.CustomerLoop.WashingBasin&)` | `System.Boolean` | `0x2ED0DB0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasValidElevator()` | `System.Boolean` | `0x2ED0FA0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.GetValidElevator(Nivalis.GhostSystem.CustomerLoop.KitchenElevatorGhost&)` | `System.Boolean` | `0x2ED1100` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.ClosestTrashBin()` | `Nivalis.GhostSystem.GhostReference`1<Nivalis.GhostSystem.CustomerLoop.TrashBinGhost>` | `0x2ED12F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.SaveInternal(Nivalis.ISaveWriter)` | `System.Void` | `0x2ED1620` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.LoadInternal(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x2ED1FC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.DayScore(Nivalis.TimeOfDayManager+TimeStamp)` | `System.Single` | `0x2ED2DF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.AvgReviewScore()` | `System.Single` | `0x2ED2FE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.set_CurrentLevel(System.Int32)` | `System.Void` | `0x2ED31C0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.AddMealServed()` | `System.Void` | `0x2ED31D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.RegisterCompletedVisit()` | `System.Void` | `0x2ED3400` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.SetLevel(System.Int32)` | `System.Void` | `0x2ED3410` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.CheckLevelUp(System.Single)` | `System.Boolean` | `0x2ED3540` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.CheckLevelDown(System.Single)` | `System.Boolean` | `0x2ED4630` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.DebugStateString()` | `System.ValueTuple`2<System.String,System.String>[]` | `0x2ED4930` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasFreeSeat(System.Boolean)` | `System.Boolean` | `0x2ED4B30` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TablesCount(System.Boolean)` | `System.Int32` | `0x2ED59A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.Initialize(Nivalis.InventorySystem.IInventory)` | `System.Void` | `0x2ED6430` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.InitializePopularityTracking()` | `System.Void` | `0x2ED64A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnFurnitureAdded(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x2ED6C40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.FireStaff(Nivalis.GhostSystem.Ai.Person)` | `System.Void` | `0x2ED6C70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HireStaff(Nivalis.GhostSystem.Ai.Person)` | `System.Void` | `0x2ED6D80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnHourUpdate()` | `System.Void` | `0x2ED6E50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.Comfort(System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemType>)` | `System.Single` | `0x2ED7810` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.Comfort()` | `System.Single` | `0x2ED7CE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnDayUpdate()` | `System.Void` | `0x2ED8100` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnCurfewStart()` | `System.Void` | `0x2ED8680` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnCurfewStartCoroutine()` | `Cysharp.Threading.Tasks.UniTask` | `0x2ED8740` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.OnCurfewEnd()` | `System.Void` | `0x2ED8800` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.SetCrime()` | `System.Void` | `0x2ED8810` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.PayStaff(Nivalis.GhostSystem.Ai.Person)` | `System.Void` | `0x2ED8EA0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasResourcesToCraftItem(Nivalis.InventorySystem.ItemType, System.Int32, System.Boolean)` | `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost+MealCraftingError` | `0x2ED9370` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasMainProcessor(Nivalis.CraftingSystem.MealRecipeDefinition)` | `System.Boolean` | `0x2ED9D10` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasIngredientProcessor(Nivalis.CraftingSystem.IngredientProcessorType)` | `System.Boolean` | `0x2ED9F70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.HasProcessors(Nivalis.CraftingSystem.MealRecipeDefinition)` | `System.Boolean` | `0x2EDA360` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.RegisterPlacedFurniture(Nivalis.GhostSystem.Ghost)` | `System.Boolean` | `0x2EDA9A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.UnregisterPickedFurniture(Nivalis.GhostSystem.Ghost, System.Boolean)` | `System.Boolean` | `0x2EDAE80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.CommitCrime(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x2EDB130` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.RegisterStorage(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x2EDB1A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.UnRegisterStorage(Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x2EDB3E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryPurchaseFurniture(Nivalis.InventorySystem.ItemType, Nivalis.GhostSystem.Ai.Person)` | `System.Boolean` | `0x2EDB670` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.BuyIngredients(System.Int32, System.Single)` | `System.Boolean` | `0x2EDBD40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryPurchaseIngredients(Nivalis.CraftingSystem.IRecipe, System.Single, System.Single)` | `System.Boolean` | `0x2EDC100` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryMakePurchase(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemType, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2EDD850` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemType, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2EDDB20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemStack, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2EDDB60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.TryMakeSale(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemStack+BasicTemp&, System.Int32, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2EDDBA0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.GetCurrentSupplyState(Nivalis.CraftingSystem.IRecipe)` | `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost+SupplyState` | `0x2EDDD60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.UpdateSupplyStates()` | `System.Void` | `0x2EDDE10` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.GetRecipeSupplyState(Nivalis.CraftingSystem.IRecipe)` | `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost+SupplyState` | `0x2EDE910` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.ReportStaffChanged()` | `System.Void` | `0x2EDF960` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.ReportStaffWorkingHoursChanged()` | `System.Void` | `0x2EDF980` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.DayIncome(System.Int32)` | `System.Single` | `0x2EDF9A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.DayCleanIncome(System.Int32)` | `System.Single` | `0x2EDFB70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.AddNewOrder(Nivalis.GhostSystem.CustomerLoop.Order)` | `Nivalis.GhostSystem.CustomerLoop.VenueOrderReference` | `0x2EDFE00` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.RemoveOrder(System.String)` | `System.Void` | `0x2EE00E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost.CheckHasWorkingHours()` | `System.Boolean` | `0x2EE0110` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueFinances.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x2EE0D20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueFinances.Load(Nivalis.SaveReader)` | `System.Void` | `0x2EE0FC0` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.CustomerLoop.VenueFinances.GetProfitsForDay(System.Int32)` | `System.Int32` | `0x2EE10E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueFinances.CalculateProfitsForDay(System.Int32)` | `System.Int32` | `0x2EE1280` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueFinances.CalculateStaffPayment(System.Int32)` | `System.Int32` | `0x2EE1480` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GetRuntimeData(Nivalis.GhostSystem.CustomerLoop.Venue)` | `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost` | `0x2EE7330` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.TriggerMenuUpdatedEvent()` | `System.Void` | `0x2EE7480` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.InitializeProviderData()` | `System.Void` | `0x2EE7540` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2EE7640` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.InitializeExternal()` | `System.Void` | `0x2EE7720` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GhostSystemInitializationListener()` | `System.Void` | `0x2EE7CC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.InitializeFromConfigurationData()` | `System.Void` | `0x2EE7DD0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.InitializeVenueRuntimeData()` | `System.Void` | `0x2EE80B0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GetClosestVenue(UnityEngine.Vector3, System.Single&)` | `Nivalis.GhostSystem.CustomerLoop.Venue` | `0x2EE8D70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnDestroyInternal()` | `System.Void` | `0x2EE9000` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnHourTick()` | `System.Void` | `0x2EE95E0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.HourlyUpdateTask(System.Threading.CancellationToken)` | `Cysharp.Threading.Tasks.UniTask` | `0x2EE9760` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnDayUpdate()` | `System.Void` | `0x2EE9860` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnCurfewStart()` | `System.Void` | `0x2EE9960` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnCurfewEnd()` | `System.Void` | `0x2EE9B20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GetOpenTimeRecord(System.Int32)` | `System.Collections.Generic.Dictionary`2<Nivalis.GhostSystem.CustomerLoop.Venue,System.Int32>` | `0x2EE9C20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GetOpenTime(System.Int32, Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Int32` | `0x2EE9E80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnRemovedOrder(Nivalis.GhostSystem.CustomerLoop.Order)` | `System.Void` | `0x2EEA040` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnMealDeliveredForOrder(Nivalis.GhostSystem.CustomerLoop.Order)` | `System.Void` | `0x2EEA260` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GetVenueByTable(Nivalis.GhostSystem.CustomerLoop.TableGhost)` | `Nivalis.GhostSystem.CustomerLoop.VenueAreaGhost` | `0x2EEA390` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.OnNewOrder(Nivalis.GhostSystem.CustomerLoop.Order)` | `System.Void` | `0x2EEA660` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.ForceAddToPlayer(Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Void` | `0x2EEA6B0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.Buy(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.IPropertyOwner)` | `System.Void` | `0x2EEA740` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.Sell(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.IPropertyOwner)` | `System.Void` | `0x2EEAA10` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.GiveVenue(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.IPropertyOwner)` | `System.Void` | `0x2EEAC90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.StartRenting(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.IPropertyOwner)` | `System.Void` | `0x2EEAE50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.StopRenting(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.IPropertyOwner)` | `System.Void` | `0x2EEAF60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.HireStaff(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.GhostSystem.Ai.Person)` | `System.Void` | `0x2EEB070` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.FireStaff(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.GhostSystem.Ai.Person)` | `System.Void` | `0x2EEB280` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.ChangeStaffTasks(Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.GhostSystem.Ai.RuntimePersonData, Nivalis.GhostSystem.Ai.VenueTasks)` | `System.Void` | `0x2EEB3F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.TryProvide(System.String)` | `UnityEngine.ScriptableObject` | `0x2EEB4B0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.RegisterPlayerVenueMenuChange(Nivalis.Locale.MealMenu)` | `System.Void` | `0x2EEB5A0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.RegisterReview(Nivalis.GhostSystem.CustomerLoop.Review)` | `System.Void` | `0x2EEB610` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.RegisterCompletedVisit(Nivalis.GhostSystem.CustomerLoop.Venue)` | `System.Void` | `0x2EEB7D0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueManager.SwitchCrime()` | `System.Void` | `0x2EEB860` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.set_State(Nivalis.GhostSystem.Ai.AgentState)` | `System.Void` | `0x31EB870` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.set_CurrentAction(Nivalis.GhostSystem.Ai.AgentAction)` | `System.Void` | `0x31EB920` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.set_CurrentActionState(Nivalis.GhostSystem.Ai.ActionState)` | `System.Void` | `0x31EB9F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.set_NextAction(System.ValueTuple`4<System.Int32,Nivalis.GhostSystem.Ai.AgentAction,Nivalis.GhostSystem.Ai.IAgentInteractable,Nivalis.GhostSystem.Ai.PersonSchedule>)` | `System.Void` | `0x31EBA80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.SetOrder(Nivalis.GhostSystem.CustomerLoop.VenueOrderReference)` | `System.Void` | `0x31EBE60` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.GetSkillActionSpeed(Nivalis.SkillSystem.SkillDefinition)` | `System.Single` | `0x31EC150` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.SaveInternal(Nivalis.ISaveWriter)` | `System.Void` | `0x31EC630` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.LoadInternal(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x31EC750` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.PostLoadInternal()` | `System.Void` | `0x31ECB00` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.UpdatePositionInternal()` | `System.Void` | `0x31ECE00` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.RegisterComplaint(Nivalis.GhostSystem.CustomerLoop.ComplaintReason)` | `System.Void` | `0x31ECE80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.FindActiveDialogues()` | `System.Collections.Generic.List`1<System.String>` | `0x31ECED0` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhost.DebugStateString()` | `System.ValueTuple`2<System.String,System.String>[]` | `0x31ED260` | 否 | 否 | 否 |
| `Nivalis.Dialogue.SetWorldObjActive.RunTrigger()` | `System.Void` | `0x7A5050` | 否 | 否 | 否 |
| `Nivalis.Dialogue.ArticyImportedWorldObj.RegisterInteraction(System.String)` | `System.Void` | `0x4E9680` | 否 | 否 | 否 |
| `Nivalis.Dialogue.ArticyImportedWorldObj.ResetToInitialValues(System.Boolean)` | `System.Void` | `0x4E97E0` | 否 | 否 | 否 |
| `Nivalis.Dialogue.ArticyImportedWorldObj.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x4E98B0` | 否 | 否 | 否 |
| `Nivalis.Dialogue.ArticyImportedWorldObj.Load(Nivalis.SaveReader)` | `System.Void` | `0x4E9960` | 否 | 否 | 是 |
| `Nivalis.Dialogue.WorldObjectGhost.DebugStateString()` | `System.ValueTuple`2<System.String,System.String>[]` | `0x2F9F790` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectGhost.SaveInternal(Nivalis.ISaveWriter)` | `System.Void` | `0x2F9F970` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectGhost.LoadInternal(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x2F9F9D0` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectGhost.Use()` | `System.Void` | `0x2F9FA40` | 否 | 否 | 是 |
| `Nivalis.Dialogue.WorldObjectView.CreateGhostBaseFromView()` | `Nivalis.GhostSystem.Ghost` | `0x2FA18A0` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.FillGhostBaseFromViewState(Nivalis.Dialogue.WorldObjectGhost, Nivalis.GhostSystem.IGhostRegistry)` | `System.Boolean` | `0x2FA18F0` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.Start()` | `System.Void` | `0x2FA1A70` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.UpdateInteractionString()` | `System.Void` | `0x2FA2280` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.WorldPointActivationChanged(Nivalis.GhostSystem.WorldPointManager+QuestPointActivationData, System.Boolean)` | `System.Void` | `0x2FA2310` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.WorldPointActivationChanged(Nivalis.Quest, System.String, System.Boolean)` | `System.Void` | `0x2FA2340` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.OnDestroy()` | `System.Void` | `0x2FA2650` | 否 | 否 | 是 |
| `Nivalis.Dialogue.WorldObjectView.OnInteracted()` | `System.Void` | `0x2FA2A10` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.Update()` | `System.Void` | `0x2FA2A20` | 否 | 是 | 否 |
| `Nivalis.Dialogue.WorldObjectView.ShouldBeActive()` | `System.Boolean` | `0x2FA2A90` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.OnWorldObjStateChange(Change`1<System.Boolean>)` | `System.Void` | `0x2FA2B60` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.RefreshActivityState(System.Boolean)` | `System.Void` | `0x2FA2B90` | 否 | 否 | 否 |
| `Nivalis.Dialogue.WorldObjectView.Use()` | `System.Void` | `0x2FA2EA0` | 否 | 否 | 是 |
| `Nivalis.Apartment.Apartment.StageStart()` | `System.Void` | `0x32016B0` | 否 | 否 | 否 |
| `Nivalis.Apartment.Apartment.StageUpdate(Nivalis.Locale.PropertyGhostBase)` | `System.Void` | `0x3201830` | 否 | 否 | 否 |
| `Nivalis.Apartment.Apartment.set_OwnerInventory(Nivalis.InventorySystem.IInventory)` | `System.Void` | `0x32024D0` | 否 | 否 | 否 |
| `Nivalis.Apartment.Apartment.set_OwnershipType(Nivalis.GhostSystem.CustomerLoop.OwnershipType)` | `System.Void` | `0x3202710` | 否 | 否 | 否 |
| `Nivalis.Apartment.Apartment.CollectFurnitureToTrash()` | `System.Void` | `0x3202740` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentAreaGhost.set_Apartment(Nivalis.Apartment.Apartment)` | `System.Void` | `0x32035A0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x6A88D0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.InitializeProviderData()` | `System.Void` | `0x6A8A70` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x6A8BF0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x6A90B0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.InitializeExternal()` | `System.Void` | `0x6A9130` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.GetState(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `Nivalis.Apartment.ApartmentState` | `0x6A9400` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.Buy(Nivalis.Apartment.Apartment, Nivalis.PlayerManager+Player)` | `System.Void` | `0x6A95B0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.Sell(Nivalis.Apartment.Apartment, Nivalis.PlayerManager+Player)` | `System.Void` | `0x6A98A0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.StartRenting(Nivalis.Apartment.Apartment, Nivalis.PlayerManager+Player, System.Boolean)` | `System.Void` | `0x6A9B70` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.StopRenting(Nivalis.Apartment.Apartment, System.Boolean)` | `System.Void` | `0x6A9E50` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.StartStaging(Nivalis.Apartment.Apartment, Nivalis.PlayerManager+Player)` | `System.Void` | `0x6AA060` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.StopStaging(Nivalis.Apartment.Apartment)` | `System.Void` | `0x6AA200` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.GetPrice(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Int32` | `0x6AA3A0` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.TryProvide(System.String)` | `UnityEngine.ScriptableObject` | `0x6AA440` | 否 | 否 | 否 |
| `Nivalis.Apartment.ApartmentManager.GetArea(Nivalis.Apartment.Apartment)` | `Nivalis.Apartment.ApartmentAreaGhost` | `0x6AA530` | 否 | 否 | 否 |

### 排除原因分布

- Construction/finalization is outside observer scope：40。
- Shared or unmapped native RVA; ambiguous entry：203。
- Editor/compiler helper or explicit interface wrapper outside scope：9。
- Accessor/event plumbing; fields or mutation endpoints observed instead：152。
- Open generic entry; observe concrete caller：26。
- Abstract/interface declaration; observe implementation：23。

## ItemHook 候选方法

| 完整方法签名 | 返回类型 | RVA | 静态 | 采样 | 跳过 After 实例读取 |
|---|---|---|---|---|---|
| `Nivalis.PlacementSpot.isInside(UnityEngine.Vector3, Nivalis.PlacementSlot, System.Single)` | `System.Boolean` | `0x9AF610` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.GetSlot(UnityEngine.Vector3, UnityEngine.Quaternion, Nivalis.PlacementSpot+SearchType, Nivalis.InventorySystem.ItemType+SlotType)` | `Nivalis.PlacementSlot` | `0x9AFBE0` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.RandomSlot(System.Collections.Generic.List`1<Nivalis.PlacementSlot>)` | `Nivalis.PlacementSlot` | `0x9AFFF0` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.ClosestSlot(System.Collections.Generic.List`1<Nivalis.PlacementSlot>, UnityEngine.Vector3)` | `Nivalis.PlacementSlot` | `0x9B00B0` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.UnOccupy(Nivalis.PlacementSlot)` | `System.Void` | `0x9B0330` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.Occupy(Nivalis.PlacementSlot, Nivalis.GhostSystem.Ghost)` | `System.Void` | `0x9B0380` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.Position(Nivalis.PlacementSlot, UnityEngine.Vector3, UnityEngine.Vector3, Nivalis.PlacementSetting+ModuleType)` | `UnityEngine.Vector3` | `0x9B07D0` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.Rotation(Nivalis.PlacementSlot, Nivalis.PlacementSetting+ModuleType)` | `UnityEngine.Quaternion` | `0x9B0E40` | 否 | 否 | 否 |
| `Nivalis.PlacementSpot.ToggleHighlight(System.Boolean, Nivalis.InventorySystem.ItemType+SlotType)` | `System.Void` | `0x9B1260` | 否 | 否 | 否 |
| `Nivalis.PlacementSlot.GetItemType()` | `Nivalis.InventorySystem.ItemType` | `0x9AF1F0` | 否 | 否 | 否 |
| `Nivalis.PlacementSlot.ToggleHighlight(System.Boolean, Nivalis.InventorySystem.ItemType+SlotType)` | `System.Void` | `0x9AF2E0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity+<PlaceRoutine>d__94.MoveNext()` | `System.Boolean` | `0x2D23390` | 否 | 是 | 是 |
| `Nivalis.HoldableEntity.TryGetFurniture(Nivalis.Locale.FurnitureObject&)` | `System.Boolean` | `0x83F090` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.set_IsBeingPlaced(System.Boolean)` | `System.Void` | `0x83F320` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.set_IsRenderedAsInvalid(Nivalis.HoldableEntity+RenderMode)` | `System.Void` | `0x83F4D0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.set_PlacementColliderOffset(UnityEngine.Vector3)` | `System.Void` | `0x83F6A0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.FindPlacementColliders()` | `System.Void` | `0x83F6C0` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.CheckValidProperty()` | `System.Boolean` | `0x83FC90` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.Awake()` | `System.Void` | `0x840020` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.Start()` | `System.Void` | `0x840490` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnEnable()` | `System.Void` | `0x8405C0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnDisable()` | `System.Void` | `0x840690` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.StartRoutine()` | `System.Collections.IEnumerator` | `0x8406B0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.Update()` | `System.Void` | `0x840710` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.OnTriggerEnter(UnityEngine.Collider)` | `System.Void` | `0x840720` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnTriggerExit(UnityEngine.Collider)` | `System.Void` | `0x840890` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnCollisionEnter(UnityEngine.Collision)` | `System.Void` | `0x840930` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnCollisionExit(UnityEngine.Collision)` | `System.Void` | `0x840A70` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.PickUp()` | `System.Boolean` | `0x840B10` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.ReturnToPickup()` | `System.Void` | `0x840E90` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.Place(System.Boolean, UnityEngine.Vector3, System.Action)` | `System.Void` | `0x841150` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.PlaceRoutine(System.Boolean, UnityEngine.Vector3, System.Action)` | `System.Collections.IEnumerator` | `0x8412E0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.UpdateState()` | `System.Void` | `0x841380` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.SetupColliderDictionaries()` | `System.Void` | `0x841450` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.SetCollidersToTrigger()` | `System.Void` | `0x8416B0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.ResetColliderTriggers()` | `System.Void` | `0x8417F0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.CheckHasOverlaps()` | `System.Boolean` | `0x841980` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.CheckColliderOverlap(UnityEngine.Collider, System.Single)` | `System.Boolean` | `0x841B50` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.SetPlacementCollider()` | `System.Void` | `0x842120` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.TurnOnGravity()` | `System.Void` | `0x8423B0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.TurnOffGravity()` | `System.Void` | `0x842410` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.ResetRotation()` | `System.Void` | `0x842470` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.RelativeRotation(UnityEngine.Transform)` | `System.Void` | `0x842900` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.OnDestroy()` | `System.Void` | `0x842B30` | 否 | 否 | 是 |
| `Nivalis.HoldableEntity.CheckParentValid(UnityEngine.Vector3)` | `System.Boolean` | `0x842B80` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.CheckParentMessageValid(UnityEngine.Vector3)` | `System.Boolean` | `0x842EA0` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.CheckSurfaceValid(System.Nullable`1<UnityEngine.RaycastHit>)` | `System.Boolean` | `0x8431A0` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.PlacementSnap(UnityEngine.Vector3)` | `System.Boolean` | `0x8431F0` | 否 | 否 | 否 |
| `Nivalis.HoldableEntity.CheckFullSurfaceValid(UnityEngine.Vector3, UnityEngine.LayerMask)` | `System.Boolean` | `0x8435F0` | 否 | 是 | 否 |
| `Nivalis.HoldableEntity.SetSnapOffset(System.Boolean)` | `System.Void` | `0x843BC0` | 否 | 否 | 否 |
| `Nivalis.PlacementSetting.CheckSurfaceValid(System.Nullable`1<UnityEngine.RaycastHit>)` | `System.Boolean` | `0x9AEC10` | 否 | 是 | 否 |
| `Nivalis.PlacementTracker.OnEnable()` | `System.Void` | `0x9B46B0` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.OnDisable()` | `System.Void` | `0x9B49F0` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.Start()` | `System.Void` | `0x9B4CA0` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.AnyObjectPlacedListener(Nivalis.HoldableEntity)` | `System.Void` | `0x9B4EA0` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.AnyHoldableEntityChangedListener(Nivalis.HoldableEntity, System.Boolean)` | `System.Void` | `0x9B5210` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.HeldStateChanged(System.Boolean)` | `System.Void` | `0x9B5520` | 否 | 否 | 否 |
| `Nivalis.PlacementTracker.DetectObjects()` | `System.Void` | `0x9B55C0` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.Start()` | `System.Void` | `0x30554C0` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.CanInteract()` | `System.Boolean` | `0x3055600` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.DoInteraction()` | `System.Void` | `0x30556A0` | 否 | 否 | 是 |
| `Nivalis.ItemCollectible.Bake()` | `System.Void` | `0x3055A50` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.Refresh()` | `System.Void` | `0x3055B10` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.RegenerateGUID()` | `System.Void` | `0x3055C70` | 否 | 否 | 否 |
| `Nivalis.ItemCollectible.ClearGUID()` | `System.Void` | `0x3055D20` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemInstanceData.get_FreshnessFactor()` | `System.Single` | `0x3104480` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemInstanceData.get_Freshness()` | `Nivalis.InventorySystem.FoodFreshness` | `0x31044E0` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemInstanceData.UpdateDecay(System.Boolean, System.Int32)` | `System.Void` | `0x3104530` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemInstanceData.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x3104590` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemInstanceData.Load(Nivalis.SaveReader)` | `System.Void` | `0x3104650` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemStack.get_StackCount()` | `System.Int32` | `0x3109600` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemStack.get_Freshness()` | `Nivalis.InventorySystem.FoodFreshness` | `0x3109640` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemStack.get_FreshnessFactor()` | `System.Single` | `0x3109780` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemStack.SafeCreate(Nivalis.InventorySystem.ItemType, System.Int32, System.Int32)` | `Nivalis.InventorySystem.ItemStack` | `0x3109880` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.SafeCreate(Nivalis.InventorySystem.ItemType, Nivalis.InventorySystem.ItemInstanceData)` | `Nivalis.InventorySystem.ItemStack` | `0x3109A30` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.SafeCreateEmpty(Nivalis.InventorySystem.ItemType, System.Int32)` | `Nivalis.InventorySystem.ItemStack` | `0x3109CF0` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.SafeCreate(Nivalis.InventorySystem.ItemType, System.Collections.Generic.IReadOnlyList`1<Nivalis.InventorySystem.ItemInstanceData>)` | `Nivalis.InventorySystem.ItemStack` | `0x3109F30` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.SafeCreate(Nivalis.InventorySystem.ItemType, System.Collections.Generic.IEnumerable`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32)` | `Nivalis.InventorySystem.ItemStack` | `0x310A1E0` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.AddInstance(Nivalis.InventorySystem.ItemInstanceData)` | `System.Void` | `0x310ABD0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.AddInstances(System.Collections.Generic.IReadOnlyList`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310AC70` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(Nivalis.InventorySystem.ItemStack)` | `System.Void` | `0x310ADD0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310AED0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(Nivalis.InventorySystem.ItemStack+BasicTemp&)` | `System.Void` | `0x310B080` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(Nivalis.InventorySystem.ItemStack, System.Int32)` | `System.Void` | `0x310B120` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32)` | `System.Void` | `0x310B1F0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.TryMerge(Nivalis.InventorySystem.ItemStack+BasicTemp&, System.Int32)` | `System.Void` | `0x310B380` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromStack(System.Int32, Nivalis.InventorySystem.ItemStack&)` | `System.Boolean` | `0x310B420` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromStack(System.Int32)` | `System.Boolean` | `0x310B4E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromStackInto(System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310B5E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromInto(System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310B650` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.GetDisplayData()` | `Nivalis.InventorySystem.ItemDisplayData` | `0x310B7B0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.GetFirstItem()` | `Nivalis.InventorySystem.ItemInstanceData` | `0x310B930` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.ToString()` | `System.String` | `0x310B990` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.PopItems(System.Int32)` | `System.Void` | `0x310BB40` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.PopItemsInto(System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310BC00` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.MoveItemsInto(System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>, System.Int32, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Void` | `0x310BD90` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.AddToStackInternal(Nivalis.InventorySystem.ItemInstanceData)` | `System.Void` | `0x310BED0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.Clear()` | `System.Void` | `0x310BF70` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromStack(Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x310C030` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.RemoveFromStack(System.Collections.Generic.IEnumerable`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x310C120` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x310C370` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.Load(Nivalis.SaveReader)` | `System.Void` | `0x310C6E0` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemStack.RemoveStaleItems()` | `System.Int32` | `0x310CAB0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemStack.UpdateItemDecay(System.Boolean, System.Int32)` | `System.Void` | `0x310CBA0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.get_HasDecay()` | `System.Boolean` | `0x310D6C0` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemType.CreateStack(System.Int32, System.Int32, Nivalis.InventorySystem.ItemStack+BasicTemp&)` | `System.Boolean` | `0x310D9D0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.InitEconomyData()` | `System.Void` | `0x310D9E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.GetShortDescription()` | `System.String` | `0x310DB30` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.GetFullDescription()` | `System.String` | `0x310DB80` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.GetFullDescription(Nivalis.InventorySystem.FoodFreshness)` | `System.String` | `0x310DB90` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.GetFullDescription(Nivalis.InventorySystem.FoodFreshness, System.Boolean)` | `System.String` | `0x310DBA0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.AddStat(System.Text.StringBuilder, System.String, System.Single, System.String)` | `System.Void` | `0x310F5F0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.AddStat(System.Text.StringBuilder, System.String, System.Int32, System.String)` | `System.Void` | `0x310F690` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.AddStatNoLabel(System.Text.StringBuilder, System.String, System.String)` | `System.Void` | `0x310F6E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.AddStat(System.Text.StringBuilder, System.String, System.String, System.String)` | `System.Void` | `0x310F7D0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemType.AddToInventory()` | `System.Void` | `0x310F8E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemInstanceCollectionExtensions.GroupCountByType(Nivalis.InventorySystem.IItemInstanceCollection)` | `System.Collections.Generic.IEnumerable`1<Nivalis.InventorySystem.ItemTypeAmount>` | `0x3103D30` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.InventorySave.Add(Nivalis.InventorySystem.ItemInstanceData)` | `System.Void` | `0x3051930` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventorySave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x3051CF0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventorySave.Load(Nivalis.SaveReader)` | `System.Void` | `0x3052150` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.InventoriesSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x30466F0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x3046910` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.InventoriesSave.Clear()` | `System.Void` | `0x3046B40` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.InventoriesManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x3045BD0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesManager.InitializeExternal()` | `System.Void` | `0x3045D60` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x3045E80` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x3045F20` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesManager.OnDestroyInternal()` | `System.Void` | `0x3046110` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.InventoriesManager.DayChangeListener()` | `System.Void` | `0x3046210` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.InventoriesManager.GetOrCreateInventory(System.String, System.Boolean, Nivalis.InventorySystem.ItemContainerRestriction, System.Boolean)` | `Nivalis.InventorySystem.ItemContainer` | `0x3046360` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.get_StackCount()` | `System.Int32` | `0x3055EE0` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemContainer.get_ItemCount()` | `System.Int32` | `0x3055F20` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryPickRandomItem(Nivalis.InventorySystem.ItemType&)` | `System.Boolean` | `0x3056700` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.UpdateRestriction(Nivalis.InventorySystem.ItemContainer)` | `System.Void` | `0x30569C0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.UpdateItemDecay(System.Boolean, System.Int32)` | `System.Int32` | `0x3056E90` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.GetItemCount(Nivalis.InventorySystem.ItemType, System.Boolean&)` | `System.Int32` | `0x3057300` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.HasItem(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x3057310` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.ContainsFurniture()` | `System.Boolean` | `0x3057540` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.GetItemCount(Nivalis.InventorySystem.ItemType)` | `System.Int32` | `0x3057720` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.GetItemCountByTag(Nivalis.InventorySystem.ObjectTag)` | `System.Int32` | `0x3057860` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryAdd(Nivalis.InventorySystem.ItemTypeAmount[])` | `System.Void` | `0x3057C60` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryAdd(Nivalis.InventorySystem.ItemStack)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x30580C0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryAdd(Nivalis.InventorySystem.ItemType, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x3058580` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryAdd(Nivalis.InventorySystem.ItemStack+BasicTemp&)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x30589B0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryAdd(Nivalis.InventorySystem.ItemInstanceData)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x3058D80` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryCreateInInventory(Nivalis.InventorySystem.ItemType, System.Int32, System.Int32)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x3058DC0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryTake(Nivalis.InventorySystem.ItemStack, System.Int32)` | `Nivalis.InventorySystem.ItemStack` | `0x3058E30` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryTake(Nivalis.InventorySystem.ItemStack, Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x30590C0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryRemoveSpecificItems(Nivalis.InventorySystem.ItemStack)` | `System.Boolean` | `0x30591A0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryTake(Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x3059CF0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.ContainsSpecificStack(Nivalis.InventorySystem.ItemStack)` | `System.Boolean` | `0x3059EC0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TryTake(Nivalis.InventorySystem.ItemStack)` | `Nivalis.InventorySystem.ItemStack` | `0x305A0B0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TakeOne(Nivalis.InventorySystem.ItemType)` | `Nivalis.InventorySystem.ItemStack` | `0x305A4F0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.GiveOne(Nivalis.InventorySystem.ItemType, System.Int32)` | `System.Void` | `0x305A730` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TakeAllByType(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x305A780` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.TakeByType(Nivalis.InventorySystem.ItemType, System.Int32, System.Collections.Generic.IList`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x305A7C0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.Clear()` | `System.Void` | `0x305AE00` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemContainer.GetEnumerator()` | `System.Collections.Generic.IEnumerator`1<Nivalis.InventorySystem.ItemStack>` | `0x305AF80` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.AcceptsItemType(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x305B140` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.GetStack(Nivalis.InventorySystem.ItemType)` | `Nivalis.InventorySystem.ItemStack` | `0x305B160` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.Refresh()` | `System.Void` | `0x305B240` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.CreateSaveData()` | `Nivalis.InventorySystem.InventorySave` | `0x305B510` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.LoadFromSaveData(Nivalis.InventorySystem.InventorySave)` | `System.Void` | `0x305B820` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x305BC30` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainer.Load(Nivalis.SaveReader)` | `System.Void` | `0x305BE00` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddItem(Nivalis.InventorySystem.ItemContainer, Nivalis.InventorySystem.ItemStack)` | `System.Boolean` | `0x3100860` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddItem(Nivalis.InventorySystem.ItemContainer, Nivalis.InventorySystem.ItemStack+BasicTemp&)` | `System.Boolean` | `0x3100940` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddItem(Nivalis.InventorySystem.ItemContainer, Nivalis.InventorySystem.ItemType, System.Int32)` | `System.Boolean` | `0x31009E0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddItemType(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x3100A90` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddItem(Nivalis.InventorySystem.ItemContainer, Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x3100C30` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.CanAddSlot(Nivalis.InventorySystem.ItemContainer)` | `System.Boolean` | `0x3100CE0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.AllowAll()` | `Nivalis.InventorySystem.ItemContainerRestriction` | `0x3100D80` | 是 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x3100DD0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemContainerRestriction.Load(Nivalis.SaveReader)` | `System.Void` | `0x3100EF0` | 否 | 否 | 是 |
| `Nivalis.InventorySystem.ItemEntity.get_Item()` | `Nivalis.InventorySystem.ItemStack` | `0x3102490` | 否 | 是 | 否 |
| `Nivalis.InventorySystem.ItemEntity.set_Item(Nivalis.InventorySystem.ItemStack)` | `System.Void` | `0x31025A0` | 否 | 否 | 否 |
| `Nivalis.InventorySystem.ItemEntity.Store(Nivalis.InventorySystem.IItemContainer)` | `System.Boolean` | `0x3102680` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.HoldableGhost.get_Item()` | `Nivalis.InventorySystem.ItemType` | `0x2D6D760` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.HoldableGhost.set_CanBeHeld(System.Boolean)` | `System.Void` | `0x2D6D990` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.HoldableGhost.set_IsHeld(System.Boolean)` | `System.Void` | `0x2D6D9F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.HoldableGhost.LoadInternal(Nivalis.SaveReader, System.String, System.String)` | `System.Boolean` | `0x2D6DAC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.set_RefridgeratedCapacity(System.Nullable`1<System.Int32>)` | `System.Void` | `0x2EE3BF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.set_NormalCapacity(System.Nullable`1<System.Int32>)` | `System.Void` | `0x2EE3D80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.ContentsChangedListener(Nivalis.InventorySystem.IItemContainer, Nivalis.InventorySystem.ItemTypeCountChange)` | `System.Void` | `0x2EE4590` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.GetEnumerator()` | `System.Collections.Generic.IEnumerator`1<Nivalis.InventorySystem.ItemStack>` | `0x2EE4610` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.get_ItemCount()` | `System.Int32` | `0x2EE4750` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.get_StackCount()` | `System.Int32` | `0x2EE47A0` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.Clear()` | `System.Void` | `0x2EE4820` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.GetItemCount(Nivalis.InventorySystem.ItemType, System.Boolean&)` | `System.Int32` | `0x2EE4860` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.GetItemCountByTag(Nivalis.InventorySystem.ObjectTag)` | `System.Int32` | `0x2EE48C0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.GetItemCount(Nivalis.InventorySystem.ItemType)` | `System.Int32` | `0x2EE4930` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryAdd(Nivalis.InventorySystem.ItemInstanceData)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x2EE4990` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryAdd(Nivalis.InventorySystem.ItemType, System.Collections.Generic.List`1<Nivalis.InventorySystem.ItemInstanceData>)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x2EE4A70` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryAdd(Nivalis.InventorySystem.ItemStack)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x2EE4AB0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryAdd(Nivalis.InventorySystem.ItemStack+BasicTemp&)` | `Nivalis.InventorySystem.ItemCollectionOperationResult` | `0x2EE4B00` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryTake(Nivalis.InventorySystem.ItemStack)` | `Nivalis.InventorySystem.ItemStack` | `0x2EE4B40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryTake(Nivalis.InventorySystem.ItemStack, System.Int32)` | `Nivalis.InventorySystem.ItemStack` | `0x2EE4B90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryTake(Nivalis.InventorySystem.ItemStack, Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x2EE4BF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryTake(Nivalis.InventorySystem.ItemInstanceData)` | `System.Boolean` | `0x2EE4CC0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TakeByType(Nivalis.InventorySystem.ItemType, System.Int32, System.Collections.Generic.IList`1<Nivalis.InventorySystem.ItemInstanceData>)` | `System.Boolean` | `0x2EE4D90` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.TryRemoveSpecificItems(Nivalis.InventorySystem.ItemStack)` | `System.Boolean` | `0x2EE4E30` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.AcceptsItemType(Nivalis.InventorySystem.ItemType)` | `System.Boolean` | `0x2EE4EB0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.Refresh()` | `System.Void` | `0x2EE4F20` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.CustomerLoop.VenueInventory.ContainsSpecificStack(Nivalis.InventorySystem.ItemStack)` | `System.Boolean` | `0x2EE4F60` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.get_IsIllegal()` | `System.Boolean` | `0x316A110` | 否 | 是 | 否 |
| `Nivalis.Locale.FurnitureObject.get_AllowPicking()` | `System.Boolean` | `0x316A230` | 否 | 是 | 否 |
| `Nivalis.Locale.FurnitureObject.set_CurrentProperty(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Void` | `0x316A810` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.CanInteract()` | `System.Boolean` | `0x316B470` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.DoInteraction()` | `System.Void` | `0x316B560` | 否 | 否 | 是 |
| `Nivalis.Locale.FurnitureObject.Awake()` | `System.Void` | `0x316B5E0` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.Start()` | `System.Void` | `0x316B6E0` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.TryRestoreCurfewLock()` | `System.Void` | `0x316BB20` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.HeldStateChangedListener(System.Boolean)` | `System.Void` | `0x316BD30` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.OnDestroy()` | `System.Void` | `0x316BE30` | 否 | 否 | 是 |
| `Nivalis.Locale.FurnitureObject.PropertyOwnerChangedListener(Nivalis.GhostSystem.CustomerLoop.BaseProperty)` | `System.Void` | `0x316C290` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.SetLock()` | `System.Void` | `0x316C430` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.ReleaseLock()` | `System.Void` | `0x316C850` | 否 | 否 | 是 |
| `Nivalis.Locale.FurnitureObject.RestoreLock()` | `System.Void` | `0x316C9E0` | 否 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.ApplyExhibitionName(UnityEngine.GameObject, System.String)` | `System.String` | `0x316CC50` | 是 | 否 | 否 |
| `Nivalis.Locale.FurnitureObject.ApplyExhibitionName(Nivalis.Locale.FurnitureObject, System.String)` | `System.String` | `0x316CE00` | 是 | 否 | 否 |

### 排除原因分布

- Shared or unmapped native RVA; ambiguous entry：125。
- Editor/compiler helper or explicit interface wrapper outside scope：8。
- Construction/finalization is outside observer scope：29。
- Accessor/event plumbing; fields or mutation endpoints observed instead：69。
- Abstract/interface declaration; observe implementation：13。
- Already covered by PlayerHook signature：4。

## GameRuntimeHook 候选方法

| 完整方法签名 | 返回类型 | RVA | 静态 | 采样 | 跳过 After 实例读取 |
|---|---|---|---|---|---|
| `Nivalis.WeatherForecastController.StopTimedWeatherProgression()` | `System.Void` | `0x2E4D2A0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.Dev_SetWeatherToPreset(Nivalis.WeatherForecastType)` | `System.Void` | `0x2E4D2B0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.Dev_OverrideWeather(Nivalis.WeatherManagerConfiguration, Nivalis.DayNightCycle.LightCycleSettings)` | `System.Void` | `0x2E4D3F0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.Dev_CurrentConfiguration()` | `Nivalis.WeatherManagerConfiguration` | `0x2E4D660` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.Dev_CurrentCycleSettings()` | `Nivalis.DayNightCycle.LightCycleSettings` | `0x2E4D790` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.OverrideWeatherForecast(System.String, System.Int32)` | `System.Void` | `0x2E4D810` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x2E4D9B0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.InitializeExternal()` | `System.Void` | `0x2E4DED0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.CreatePacket()` | `Nivalis.ISavePacket` | `0x2E4E000` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x2E4E110` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.OnWeatherAlarmChanged(System.String, System.Object)` | `System.Void` | `0x2E4E2B0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.ForceUpdateLocalWeatherManager()` | `System.Void` | `0x2E4E3F0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.SceneLoadCompletedListener()` | `System.Void` | `0x2E4E470` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.OnDestroyInternal()` | `System.Void` | `0x2E4E480` | 否 | 否 | 是 |
| `Nivalis.WeatherForecastController.UpdateWeatherForCurrentTime(Change`1<Nivalis.TimeOfDayManager+TimeStamp>)` | `System.Void` | `0x2E4E6F0` | 否 | 是 | 否 |
| `Nivalis.WeatherForecastController.GetForecastIndex(Nivalis.TimeOfDayManager+TimeStamp)` | `System.Int32` | `0x2E4F120` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.GetCurrentWeatherConfiguration(System.Int32)` | `Nivalis.WeatherManagerConfiguration` | `0x2E4F150` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.GetWeatherSceneIndex(Nivalis.Weather.WeatherManager)` | `System.Int32` | `0x2E4F210` | 是 | 否 | 否 |
| `Nivalis.WeatherForecastController.GetWeatherConfigurationForTime(Nivalis.TimeOfDayManager+TimeStamp, System.Int32)` | `Nivalis.WeatherManagerConfiguration` | `0x2E4F3C0` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.GetConfiguration(System.Int32, System.Int32)` | `Nivalis.WeatherManagerConfiguration` | `0x2E4F440` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.TransitionWeatherFrom(Nivalis.WeatherForecastType)` | `Nivalis.WeatherForecastType` | `0x2E4F780` | 否 | 否 | 否 |
| `Nivalis.WeatherForecastController.GetForecast(System.Int32, System.Int32)` | `Nivalis.WeatherForecastType` | `0x2E4FA80` | 否 | 否 | 否 |
| `Nivalis.SavedObject.UpdateScene()` | `System.Void` | `0x3193C70` | 否 | 否 | 否 |
| `Nivalis.SavedObject.OnDestroy()` | `System.Void` | `0x3193E20` | 否 | 否 | 是 |
| `Nivalis.SavedObject.RestoreDefaults()` | `System.Void` | `0x3193E60` | 否 | 否 | 否 |
| `Nivalis.SavedObject.Load(ES2Reader)` | `System.Void` | `0x31940C0` | 否 | 否 | 是 |
| `Nivalis.SavedObject.Save(ES2Writer)` | `System.Void` | `0x3194CD0` | 否 | 否 | 否 |
| `Nivalis.SavedObject.FindValue(System.Int32)` | `Nivalis.SavedValue` | `0x3194FA0` | 否 | 是 | 否 |
| `Nivalis.SavedObject.AddValue(Nivalis.SavedValue)` | `System.Void` | `0x3195070` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.CompareGuid(System.String)` | `System.Boolean` | `0x79A620` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.Awake()` | `System.Void` | `0x79A6D0` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.Start()` | `System.Void` | `0x79A910` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.OnDisable()` | `System.Void` | `0x79ACF0` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.OnDestroy()` | `System.Void` | `0x79ADA0` | 否 | 否 | 是 |
| `Nivalis.SerializableObject.OnPoolingPostDestroy()` | `System.Void` | `0x79AFE0` | 否 | 否 | 是 |
| `Nivalis.SerializableObject.SetupSavedValues(Nivalis.SavedObject)` | `System.Void` | `0x79B060` | 否 | 否 | 否 |
| `Nivalis.SerializableObject.OnAfterLoad()` | `System.Void` | `0x79B990` | 否 | 否 | 否 |
| `Nivalis.SerializationManager+<UpdateSceneObjects>d__19.MoveNext()` | `System.Boolean` | `0x2D18860` | 否 | 是 | 是 |
| `Nivalis.SerializationManager+<LoadRoutine>d__42.MoveNext()` | `System.Boolean` | `0x2D16400` | 否 | 是 | 是 |
| `Nivalis.SerializationManager.Awake()` | `System.Void` | `0x79CAA0` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.Clear()` | `System.Void` | `0x79CBA0` | 否 | 否 | 是 |
| `Nivalis.SerializationManager.OnDestroyInternal()` | `System.Void` | `0x79CBF0` | 否 | 否 | 是 |
| `Nivalis.SerializationManager.RegisterObject(Nivalis.SerializableObject)` | `System.Void` | `0x79CCE0` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.RegisterObject(System.String, Nivalis.SavedObject)` | `System.Void` | `0x79CE80` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.GetSavedObject(System.String)` | `Nivalis.SavedObject` | `0x79CF00` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.OnSceneLoaded(UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode)` | `System.Void` | `0x79CFD0` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.UpdateSceneObjects(System.Int32)` | `System.Collections.IEnumerator` | `0x79D090` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.ForceInitializeSerializedObject(System.String, System.Int32)` | `System.Void` | `0x79D110` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.SaveGlobalSave()` | `System.Void` | `0x79D640` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.GetGlobalSaveReader(Nivalis.SerializationManager+GlobalSaveHeader&)` | `Nivalis.SaveReader` | `0x79D950` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.LoadScreenshot(System.String)` | `UnityEngine.Texture2D` | `0x79DB00` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.EnumerateSaveFiles()` | `System.Collections.Generic.IEnumerable`1<System.String>` | `0x79DD00` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.HasAnyValidSave()` | `System.Boolean` | `0x79DE40` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.GetValidSaves(System.Collections.Generic.List`1<System.ValueTuple`2<System.String,Nivalis.SerializationManager+SaveHeader>>&)` | `Nivalis.ListPool`1+Handle<System.ValueTuple`2<System.String,Nivalis.SerializationManager+SaveHeader>>` | `0x79E0E0` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.LoadSaveHeader(System.String, Nivalis.SerializationManager+SaveHeader&, Nivalis.SerializationManager+LoadingInfo)` | `System.Boolean` | `0x79E580` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.CaptureView(System.String)` | `System.Void` | `0x79EDD0` | 是 | 否 | 否 |
| `Nivalis.SerializationManager.Save(System.String, System.Boolean)` | `System.Boolean` | `0x79F360` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.Load(System.String)` | `System.Void` | `0x7A0CF0` | 否 | 否 | 是 |
| `Nivalis.SerializationManager.DoesSaveExist(System.String)` | `System.Boolean` | `0x7A0E20` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.DeleteSave(Nivalis.SaveSlotUi, System.String)` | `System.Void` | `0x7A0FF0` | 是 | 否 | 否 |
| `Nivalis.SerializationManager.LoadRoutine(System.String)` | `System.Collections.IEnumerator` | `0x7A1360` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.DeregisterObject(System.String)` | `System.Void` | `0x7A13E0` | 否 | 否 | 是 |
| `Nivalis.SerializationManager.SaveSettingsIfNeeded()` | `System.Void` | `0x7A1440` | 否 | 否 | 否 |
| `Nivalis.SerializationManager.LoadSettings()` | `System.Void` | `0x7A17F0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.TimeInSeconds(UnityEngine.Vector2Int)` | `System.Int32` | `0x3148540` | 是 | 否 | 否 |
| `Nivalis.TimeOfDayManager.InitializeInternal(Nivalis.ManagerConfiguration, Nivalis.ISavePacket)` | `System.Void` | `0x31485A0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.InitializeExternal()` | `System.Void` | `0x3148A40` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.CreatePacket()` | `Nivalis.ISavePacket` | `0x3148A50` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.WriteToPacket(Nivalis.ISavePacket)` | `System.Void` | `0x3148AD0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.OnDestroyInternal()` | `System.Void` | `0x3148B40` | 否 | 否 | 是 |
| `Nivalis.TimeOfDayManager.Update()` | `System.Void` | `0x3148C30` | 否 | 是 | 否 |
| `Nivalis.TimeOfDayManager.AddTime(System.Single)` | `System.Void` | `0x3148D50` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.UpdateStaticVariables()` | `System.Void` | `0x3148DD0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.OnTimeUpdate(Change`1<Nivalis.TimeOfDayManager+TimeStamp>)` | `System.Void` | `0x3149140` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Sleep(Nivalis.TimeOfDayManager+TimeStamp)` | `System.Void` | `0x3149470` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Pause(System.Object)` | `Nivalis.OverrideableBool+OverrideLock` | `0x31494F0` | 是 | 否 | 否 |
| `Nivalis.TimeOfDayManager.InternalOnPauseChanged()` | `System.Void` | `0x31495B0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.GetPartOfDay(System.Int32)` | `Nivalis.PartOfDay` | `0x3149630` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_SetGameTimeMultiplier(System.Single, System.Single)` | `System.Void` | `0x31496B0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_CurrentFractionOfDay()` | `System.Single` | `0x3149780` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_SetTime(System.Single, System.Boolean)` | `System.Void` | `0x31497E0` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_SetTime(System.Int32, System.Int32, System.Int32)` | `System.Void` | `0x3149940` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_CurrentTimeStamp()` | `Nivalis.SerializableTimeStamp` | `0x3149A90` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.SetTime(Nivalis.SerializableTimeStamp)` | `System.Void` | `0x3149B30` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_Pause()` | `System.Void` | `0x3149B90` | 否 | 否 | 否 |
| `Nivalis.TimeOfDayManager.Dev_UnPause()` | `System.Void` | `0x3149C40` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool+Pool.QuickPrewarm(System.Int32, System.Boolean)` | `System.Void` | `0x2D68670` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool+Pool.FullPrewarm(System.Int32, System.Boolean)` | `System.Void` | `0x2D68840` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool+Pool.TryPop(Nivalis.PooledElement&)` | `System.Boolean` | `0x2D689E0` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool+Pool.Release(Nivalis.PooledElement)` | `System.Void` | `0x2D68A80` | 否 | 否 | 是 |
| `Nivalis.GameObjectPool.CanBePooled(UnityEngine.GameObject)` | `System.Boolean` | `0x3177540` | 是 | 否 | 否 |
| `Nivalis.GameObjectPool.set_Global(Nivalis.GameObjectPool)` | `System.Void` | `0x3177740` | 是 | 否 | 否 |
| `Nivalis.GameObjectPool.AvailableInPool(UnityEngine.GameObject)` | `System.Boolean` | `0x31778A0` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool.GetPool(UnityEngine.GameObject)` | `Nivalis.GameObjectPool+Pool` | `0x31779B0` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool.Get(UnityEngine.GameObject, UnityEngine.Vector3, UnityEngine.Quaternion)` | `Nivalis.PooledElement` | `0x3177B20` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool.Get(UnityEngine.GameObject, UnityEngine.Vector3, UnityEngine.Quaternion, UnityEngine.Transform)` | `Nivalis.PooledElement` | `0x31780C0` | 否 | 否 | 否 |
| `Nivalis.GameObjectPool.GetRuntimeTemplate(UnityEngine.GameObject)` | `UnityEngine.GameObject` | `0x31786F0` | 是 | 否 | 否 |
| `Nivalis.GameObjectPool.Get(UnityEngine.GameObject, UnityEngine.Transform)` | `Nivalis.PooledElement` | `0x31787B0` | 否 | 否 | 否 |
| `Nivalis.PooledElement.ReleaseAllBeforeUnloadingScene(UnityEngine.SceneManagement.Scene)` | `System.Void` | `0x2DD00B0` | 是 | 否 | 是 |
| `Nivalis.PooledElement.Release()` | `System.Void` | `0x2DD08F0` | 否 | 否 | 是 |
| `Nivalis.PooledElement.CaptureInitialState()` | `System.Void` | `0x2DD0A20` | 否 | 否 | 否 |
| `Nivalis.PooledElement.ResetToDefault()` | `System.Void` | `0x2DD0F50` | 否 | 否 | 否 |
| `Nivalis.PooledElement.Awake()` | `System.Void` | `0x2DD18E0` | 否 | 否 | 否 |
| `Nivalis.PooledElement.Init(UnityEngine.GameObject, Nivalis.GameObjectPool+Pool, System.Boolean)` | `System.Void` | `0x2DD1C90` | 否 | 否 | 否 |
| `Nivalis.PooledElement.Destroy(UnityEngine.Component)` | `System.Void` | `0x2DD1E90` | 否 | 否 | 是 |
| `Nivalis.PooledElement.DestroyImmediate(UnityEngine.Component)` | `System.Void` | `0x2DD20A0` | 否 | 否 | 是 |
| `Nivalis.PooledElement.Destroy(UnityEngine.GameObject)` | `System.Void` | `0x2DD22B0` | 否 | 否 | 是 |
| `Nivalis.PooledElement.DestroyImmediate(UnityEngine.GameObject)` | `System.Void` | `0x2DD2610` | 否 | 否 | 是 |
| `Nivalis.PooledElement.TriggerOnDestroy()` | `System.Void` | `0x2DD2970` | 否 | 否 | 是 |
| `Nivalis.PooledElement.TriggerOnPostDestroy()` | `System.Void` | `0x2DD2D70` | 否 | 否 | 是 |
| `Nivalis.PooledElement.OnDestroy()` | `System.Void` | `0x2DD3000` | 否 | 否 | 是 |
| `Nivalis.Weather.WeatherManager.set_LastGameplaySceneIndex(System.Int32)` | `System.Void` | `0x2E50CE0` | 是 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.set__instance(Nivalis.Weather.WeatherManager)` | `System.Void` | `0x2E50DA0` | 是 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.Awake()` | `System.Void` | `0x2E511F0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.OnDestroy()` | `System.Void` | `0x2E51410` | 否 | 否 | 是 |
| `Nivalis.Weather.WeatherManager.Start()` | `System.Void` | `0x2E515A0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.OnEnable()` | `System.Void` | `0x2E51960` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateWeather()` | `System.Void` | `0x2E51F00` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateSnowSettings()` | `System.Void` | `0x2E52650` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateRainSettings()` | `System.Void` | `0x2E53050` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.Remap01(System.Single, System.Single, System.Single)` | `System.Single` | `0x2E54930` | 是 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.ComputeVectorProperties(System.Single)` | `System.Void` | `0x2E54940` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.ComputeFloatProperties(System.Single)` | `System.Void` | `0x2E54BC0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.EnsureCameraIsFound()` | `System.Void` | `0x2E54DC0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateRainParticles()` | `System.Void` | `0x2E54FD0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateSnowParticles()` | `System.Void` | `0x2E55690` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateSnowSmokeParticles()` | `System.Void` | `0x2E56010` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateThunder()` | `System.Void` | `0x2E56880` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.StartThunder()` | `System.Void` | `0x2E56B60` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.EndThunder()` | `System.Void` | `0x2E576F0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.THUNDER_Update()` | `System.Void` | `0x2E57B60` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.FixedUpdate()` | `System.Void` | `0x2E57F80` | 否 | 是 | 否 |
| `Nivalis.Weather.WeatherManager.RemoteOverrideNextLightningIndex(System.Int32)` | `System.Void` | `0x2E57FE0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.RemoteInvokeLightingBolt()` | `System.Void` | `0x2E57FF0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.UpdateAllWeather()` | `System.Void` | `0x2E58010` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.IsInSnow(UnityEngine.RaycastHit)` | `System.Boolean` | `0x2E58050` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.IsInPuddle(UnityEngine.RaycastHit)` | `System.Boolean` | `0x2E583B0` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.SampleMOHSMap(UnityEngine.Material, UnityEngine.Vector2)` | `System.Single` | `0x2E58C50` | 否 | 否 | 否 |
| `Nivalis.Weather.WeatherManager.Update()` | `System.Void` | `0x2E58FD0` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.Awake()` | `System.Void` | `0x31EDA40` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.Start()` | `System.Void` | `0x31EDB50` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.OnDestroy()` | `System.Void` | `0x31EDC80` | 否 | 否 | 是 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.InitializeExternal()` | `System.Void` | `0x31EDEE0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.CurrentTimeOnValueChanged(Change`1<Nivalis.TimeOfDayManager+TimeStamp>)` | `System.Void` | `0x31EDEF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.OnCurfewEnd()` | `System.Void` | `0x31EE000` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.Update()` | `System.Void` | `0x31EE140` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.UpdateAgentActions(System.Single)` | `System.Void` | `0x31EE1F0` | 否 | 是 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.UpdateCurrentAgentAction(Nivalis.GhostSystem.Ai.AgentGhost, System.Single)` | `System.Void` | `0x31EE540` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.SelectNewAction(Nivalis.GhostSystem.Ai.AgentGhost, System.Boolean)` | `System.Void` | `0x31EEA80` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.SwitchToDialogueAction(Nivalis.GhostSystem.Ai.AgentGhost)` | `System.Void` | `0x31EF180` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.SwitchAction(Nivalis.GhostSystem.Ai.AgentGhost, Nivalis.GhostSystem.Ai.AgentActionType, System.Int32, Nivalis.GhostSystem.Ai.PersonSchedule)` | `System.Boolean` | `0x31EF350` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.TryConstructAction(Nivalis.GhostSystem.Ai.AgentGhost, Nivalis.GhostSystem.Ai.AgentActionType, Nivalis.GhostSystem.Ai.PersonSchedule)` | `System.ValueTuple`2<Nivalis.GhostSystem.Ai.AgentAction,Nivalis.GhostSystem.Ai.IAgentInteractable>` | `0x31EF5F0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.SwitchAgentAction(System.Int32, Nivalis.GhostSystem.Ai.AgentGhost, Nivalis.GhostSystem.Ai.AgentAction, Nivalis.GhostSystem.Ai.IAgentInteractable, Nivalis.GhostSystem.Ai.PersonSchedule)` | `System.Boolean` | `0x31EF710` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.EnsureInitialized(Nivalis.GhostSystem.Ai.AgentGhost)` | `System.Void` | `0x31EFAA0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.SkipTime(System.Single, System.Boolean)` | `System.Void` | `0x31EFEF0` | 否 | 否 | 否 |
| `Nivalis.GhostSystem.Ai.AgentGhostSimulator.ListCurrentActionStatistics()` | `System.Void` | `0x31F03B0` | 否 | 否 | 否 |
| `Nivalis.ManagersSave.Save(Nivalis.ISaveWriter)` | `System.Void` | `0x30F1B30` | 否 | 否 | 否 |
| `Nivalis.ManagersSave.Load(Nivalis.SaveReader)` | `System.Void` | `0x30F1F50` | 否 | 否 | 是 |
| `Nivalis.DayNightCycle.LightCycleManager.set_CurrentTime(System.Single)` | `System.Void` | `0x311D040` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.set_CycleDuration(System.Single)` | `System.Void` | `0x311D090` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.SetSceneOverrides(Nivalis.DayNightCycle.SceneFogSettings)` | `System.Void` | `0x311D0B0` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.OnDestroy()` | `System.Void` | `0x311D2D0` | 否 | 否 | 是 |
| `Nivalis.DayNightCycle.LightCycleManager.BicubicInterpolation01(System.Single, System.Single)` | `System.Single` | `0x311D4D0` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.ComputeLightCycleOutput(Nivalis.DayNightCycle.LightCycleSettings)` | `Nivalis.DayNightCycle.LightCycleOutput` | `0x311D500` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.ApplyLightCycleOutput(Nivalis.DayNightCycle.LightCycleOutput)` | `System.Void` | `0x311E480` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.ApplySkySettings(Nivalis.DayNightCycle.LightCycleSettings, System.Single)` | `System.Void` | `0x311FCE0` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.ApplySkySettingsCoroutine(Nivalis.DayNightCycle.LightCycleSettings, System.Single)` | `System.Collections.IEnumerator` | `0x311FE90` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.UpdateLighting(System.Boolean)` | `System.Void` | `0x311FF30` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.SetTime(System.Single)` | `System.Void` | `0x3120840` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.SetTime(System.Int32, System.Int32)` | `System.Void` | `0x31208D0` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.Start()` | `System.Void` | `0x3120900` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.LightingUpdateHack()` | `System.Collections.IEnumerator` | `0x3120AE0` | 否 | 否 | 否 |
| `Nivalis.DayNightCycle.LightCycleManager.Update()` | `System.Void` | `0x3120B40` | 否 | 是 | 否 |

### 排除原因分布

- Shared or unmapped native RVA; ambiguous entry：85。
- Construction/finalization is outside observer scope：24。
- Editor/compiler helper or explicit interface wrapper outside scope：13。
- Iterator plumbing; observe MoveNext：1。
- Accessor/event plumbing; fields or mutation endpoints observed instead：11。
- Abstract/interface declaration; observe implementation：2。
