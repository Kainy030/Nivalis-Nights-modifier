# NivalisNightsTrainer — 快速交接

## 最新交接：v1.1-Dev（2026-10-03）

- 当前最新源码版本为 `v1.1-Dev`，基于游戏升级后的新 `GameAssembly.dll` 与 `global-metadata.dat` 哈希；HookRuntime 与修改器校验已同步更新。
- 修改器命令与玩家探测日志已增强：金币命令记录 ACK、完成/拒绝/超时/失败结果；玩家探测同样记录结果与超时；无效金币输入会明确拒绝并提示安全范围。
- PlayerHook 清单更新为 323 个方法、32 个类型、48 个明确排除项；PlayerHook 323、WorldHook 349、ItemHook 220、GameRuntimeHook 174，共 1,066 个 v1.1 候选入口。
- 当前发布包为 `outputs/BuildRelease/v1.1-Dev`，入口 `NivalisNightsTrainer.exe`，`build.json` 标记版本 `v1.1-Dev`，包含 231 个 payload 文件。
- 已清理旧发布目录 `outputs/BuildRelease/v0.5.2-CommandFix`、`v0.6-Dev`、`v1.0-Dev`；保留当前 `v1.1-Dev` 及源码、当前 v1.1 反编译导出和生成清单。
- v1.1 Catalog 已根据当前导出重新生成；旧版 RVA/旧 disassembly-index 不再参与构建。构建包存在不等于已完成真实游戏内注入、玩法或跨电脑验证；如未有新的运行证据，不应宣称这些行为已验证。

以下为历史交接记录；如与本节冲突，以本节为准。
# NivalisNightsTrainer — 快速交接

## 最新交接：v0.5.1-BugFix（2026-10-01）

- 工作区根目录已由用户改名为 `D:/NivalisNightsTrainer`，旧 `D:/NivalisNightsTrainer` 已不存在。下方历史路径保留为历史记录；后续命令使用新根目录。文档索引位于 `outputs/catalog.md`。
- GUI 程序集和发布入口由 `NightsHack` 改为 `NivalisNightsTrainer`，构建脚本的 build.json Entry 和使用说明已同步；内部项目目录、命名空间、插件 ID、IPC 和游戏内部署路径保持原标识。
- 修复已注入状态再次点击“手动注入”导致基础选项锁定：RunAction 在重置目标 PID 和页面状态之前，检查基础选项已就绪且原游戏会话仍存活；成立时记录 `INJECTION_ALREADY_READY` 并直接返回，保留会话和基础选项状态。会话失效时仍执行原有校验和锁定流程。
- 回归检查在模拟有效游戏会话下连续触发两次真实按钮 Click，核对基础选项仍可选、按钮仍可用及提示日志；原有会话失效锁定检查仍保留。这是 UI 状态回归检查，不是真实游戏注入验证。
- 已执行 `Build-Modifier.ps1 -Version v0.5-ReinjectFix -SkipHooks`，Bootstrap 构建零警告/错误，13 项修改器检查通过，GUI 发布成功。完整包为 `D:/NivalisNightsTrainer/outputs/BuildRelease/v0.5-ReinjectFix`，入口 `NivalisNightsTrainer.exe`，build.json 记录 231 个 payload 文件。本轮未重建四插件，未重新注入或进行游戏内复测。
- 本轮源码发布标签为 `v0.5.1-BugFix`；构建包版本仍为 `v0.5-ReinjectFix`。本交接随修复代码提交到 main，并以最终 Git 远端核对结果确认推送及附注标签状态，不移动既有标签。
- 根目录改名后，本会话沙箱仍保留旧路径授权；普通构建曾因 obj 临时文件写入被拒绝，随后通过正常提权审批完成构建。自动审批服务曾超时/断流；不得绕过审批或将未执行操作视作成功。

以下为历史交接，当前名称、路径和重复注入修复以上述记录为准；赞助图片使用限制继续有效。

## 最新交接补充：v0.5.2-BugFix（2026-10-01）

- 跨电脑运行时注入和四个钩子均可正常就绪，但玩家探测/改钱失败的原因已确认：`PlayerHook` 的 `Commands.Enabled` 默认值为 `false`，新电脑生成的 BepInEx 配置因此没有启动本地命令管道。
- `src/NightsHack.PlayerHook/PlayerHook.cs` 已将该默认值改为 `true`。现有机器若 `BepInEx/config/nightshack.playerhook.cfg` 仍有 `Enabled = false`，需改为 `true` 或删除该配置后重启游戏。命令服务是 Trainer 玩家探测和余额设置的必要组件。
- `src/NightsHack.PlayerHook/README.md` 已记录配置行为和恢复步骤。其余插件、命名空间、IPC 名称和部署目录保持不变。
- 已执行 `Build-Hooks.ps1 -Restore`：四插件和公共运行库构建零警告/错误；PlayerHook 检查 16 项通过，跨插件检查 44 项通过。已执行 `Build-Modifier.ps1 -Version v0.5.2-CommandFix -SkipHooks`：修改器检查 13 项通过，发布成功。
- 新发布包为 `D:/NivalisNightsTrainer/outputs/BuildRelease/v0.5.2-CommandFix`，入口 `NivalisNightsTrainer.exe`。静态检查不等于另一台电脑的实际注入/玩法验证，本轮仍未进行跨机器运行复测。
- 本轮源码发布标签为 `v0.5.2-BugFix`；提交和远端状态以 Git 最终核对结果为准。

## 历史交接：v0.5-UIFix（2026-10-01）

- 本轮已完成并准备发布：首页包含“手动注入”“探测玩家数据”“无偿捐赠作者”，三个按钮统一为 `180×42`；“探测玩家数据”不再位于基础选项页。
- 基础选项改为“修改余额为 [数值] [确定]”。用户输入为显示金币，内部按 `1 金币 = 100` 个游戏整数单位传输；已验证 `PlayerInventory.Money` 是 `int`，安全可写范围为游戏单位 `1..2147483646`，因此界面范围为 `0.01..21474836.46`，不允许超过安全上限后溢出归零。
- 余额命令不再使用增加/减少 delta，而是通过本地命名管道 `NightsHack.Command.<PID>` 发送 `Set` 绝对值命令。PlayerHook 在 `Nivalis.PlayerManager.Update()` 的游戏线程回调中排队、解析当前 `LocalPlayer.Inventory` 实例、调用 `Money` setter 并回读结果。
- 命令审计事件（接收、目标校验、执行、余额写入、完成/拒绝/失败）由 PlayerHook 同时输出到 BepInEx 控制台和游戏目录的 hooks JSONL；注入器窗口及 `logs/injector-*.jsonl` 只保留注入生命周期与钩子状态日志，不再写入玩法命令结果。
- 源码涉及 `src/NightsHack.Modifier/MainForm.cs`、`src/NightsHack.PlayerHook/PlayerHook.cs`、`src/NightsHack.HookRuntime/HookAudit.cs`、新增 `src/NightsHack.HookRuntime/CommandRuntime.cs`，并同步更新修改器、PlayerHook 和根 README。旧的“无 IPC/无改钱实现”描述仅保留在历史记录中，不代表当前状态。
- 发布目录：`D:/NivalisNightsTrainer/outputs/BuildRelease/v0.5-Dev-UI-Audit2`，入口为 `NightsHack.exe`。本轮源码提交为 `a18b5fa`，用户要求的 Git 标签为 `v0.5-UIFix`；标签与远端状态已按本交接更新后的收尾步骤执行，并以 Git 核对结果为准。
- 验证结果：`Build-Hooks.ps1` 构建四插件和公共运行库，16 项 HookRuntime 检查与 44 项跨插件检查通过，零警告/错误；`Build-Modifier.ps1 -Version v0.5-Dev-UI-Audit2 -SkipHooks` 的 13 项修改器检查通过，发布包包含 231 个 payload 文件。静态检查和构建不等于游戏内集成验证；本轮未用新包重新注入并实测余额写入。

## 赞助图片使用限制（用户明确要求，后续接手必须遵守）

- `D:\Pay` 下的两张赞助图片仅允许在构建发布包时由打包流程读取、复制和打包，不得用于其他用途。
- 禁止代理查看图片内容：不得打开、预览、渲染、截图查看、OCR、识别二维码或提取、分析图片中的信息。
- 此限制同样适用于工作区、临时目录和发布包中的图片副本，不得通过查看副本或打开 Trainer 捐赠弹窗等方式绕过限制。
- 打包检查仅可核对文件数量、存在性及大小等文件属性；不得以构建验证、UI 验收或排错为由查看内容。后续构建授权不代表解除此禁令，除非用户明确修改该要求。

v0.4.2-Dev-Fix：注入就绪、注入完成及就绪审计失败提示明确列出 PlayerHook、WorldHook、ItemHook、GameRuntimeHook。Bootstrap 编译和 GUI 发布成功，12 项修改器检查通过，231 个 payload 文件哈希一致；未重新注入游戏。原 v0.4.2-Dev 发布目录被运行中进程占用，完整新包保存在 outputs/BuildRelease/v0.4.2-Dev-LogNames；程序版本号仍为 v0.4.2-Dev，Fix 为源码发布标签。

2026-09-30 最新 v0.4.2-Dev：界面只保留“手动注入”和注入器日志。删除游戏路径/浏览区域、两句说明、实时钩子日志页及其轮询定时器；按运行中 Nivalis Nights 进程定位 PID，后台读取进程路径完成原有校验和部署，拒绝无目标/多个目标并校验 PID+启动时间避免换进程。既有钩子日志输出和审计链保持不变。成品 outputs/BuildRelease/v0.4.2-Dev；构建与 12 项修改器检查通过，UI 已渲染核对，本轮没有重启游戏或重新注入。下方双页日志说明为历史记录。

最终发布包复验（2026-09-30 22:55 +08:00）：PID 38628 正常启动后确认 CoreCLR 未自动加载；动态注入成功，请求 ID 035ed955-3fce-465c-a283-fa05be405bce。GUI 实际读取本会话九条记录，四个 HOOK_READY 均显示为 Passive、observationPatches=0、snapshots=false、exportTimer=false，进程有响应。231 个 payload 文件 SHA256 全部一致，静态启动文件数 0。

2026-09-30 最新修改器状态（覆盖下方 v0.4 双模式记录）：v0.4.1-Dev 仅动态注入，标题 Kainy's Nivalis Nights Trainer · v0.4.1-Dev。静态按钮/API/发布包启动文件已移除；本机旧 winhttp.dll、doorstop_config.ini、.doorstop_version 已按哈希确认并备份移至 work/static-loader-retired/20260930-b00da622f6e9489fb2fe6c6294c05b17。用户保存授权后正常重启，PID 39244 未自动加载 CoreCLR，随后新版动态注入成功，四个被动钩子 0 观察。审计日志 BepInEx/logs/NightsHack/hooks-{PID}-{StartTimeUtcTicks}.jsonl，注入请求 GUID 贯通共享内存/Bootstrap/HookAudit，成功必须有本次四个 HOOK_READY。GUI 实际测试确认当前会话九条日志、四个 HOOK_READY 显示；修复正反斜杠路径匹配问题。新增 ExecuteCommand/HookAudit.Execute 按事件记录未来功能请求、目标验证、调用及明确结果，异常区分 NotExecuted/SideEffectsUnknown，不新增常驻原版方法拦截；没有实现 IPC/线程调度/具体玩法。58+11 检查通过，实际主菜单加载/日志已验证，存档玩法与长期性能未复测。成品 outputs/BuildRelease/v0.4.1-Dev，未提交/推送。详见 src/NightsHack.Modifier/README.md。


更新：2026-09-30。工作区：`D:/NivalisNightsTrainer`。目标：Nivalis Nights 单机修改器／调试工具。

## 仓库与文档入口（v0.1-Dev）

- 项目开源协议为 GPL-3.0-only，完整条款在根目录 LICENSE；README 已提供链接。许可证作为后续提交发布，已存在的 v0.1-Dev 标签不移动。

- 远端仓库：https://github.com/Kainy030/Nivalis-Nights-modifier；本次发布目标为 main 分支及带注释的 v0.1-Dev 标签，实际推送结果以 Git/远端状态为准。
- 根目录 [README.MD](../README.MD) 概括项目、模块、当前限制及本地构建前置条件。
- [catalog.md](catalog.md) 是 HOOKS-REFERENCE.md 和 REFERENCE.md 的完整标题索引，不复制详细方法。新增或重命名章节后同步更新索引。
- 版本范围为四插件和公共运行库的观察基础设施；修改器本体未实现。本机已部署并进行有限实测，具体边界见最新状态。
- .gitignore 排除游戏二进制、完整元数据/反编译导出、SDK、第三方依赖、缓存和编译产物；仓库保留自有源码、候选目录、检查程序、文档与分析工具。完整本地构建/静态检查仍需自行准备 README 所列输入。

## 最新状态与下一步

2026-09-30 修改器 v0.4-Dev：在 v0.3-Dev-LagFix（7db63f4，已推送）基础上新增 src/NightsHack.Modifier（WinForms）、NightsHack.Native（一次性 WH_GETMESSAGE 原生引导）、NightsHack.Bootstrap（net6 托管初始化）和 8 项修改器检查。Build-Modifier.ps1 输出 outputs/BuildRelease/v0.4-Dev，携带私有 GUI 运行时与既有 BepInEx 依赖，不含游戏程序集。静态按钮要求退出游戏，版本/包双层校验、备份/回滚、保留配置；动态按钮要求运行中且未加载 CoreCLR，不安装启动代理，不重复初始化。动态在 PID 35320 主菜单实测载入四插件且恢复响应，四插件仍默认 0 观察；随后静态校验和正常启动 PID 11152 确认四插件加载。仅验证主菜单/现有互操作缓存，存档玩法和首次缓存生成尚未验证。此前减负版已部署且用户反馈性能问题彻底解决；不要把下方旧记录的“未部署/未验证”当作当前状态。本轮修改器代码尚未提交推送，未创建新标签；详见 src/NightsHack.Modifier/README.md。

2026-09-30 最新减负架构：Catalog.Load 恢复完整 1067 个被动入口（324/350/220/173），视觉 183 和 AI 30 重新作为目录资料保留，不再因目录存在而安装。默认 Features.AllowList 为空、Diagnostics.Enabled=false，空选择在原生上下文、NativeSnapshots、Harmony 和 Timer 之前返回；旧 Hook.Enabled/Groups/Export 配置不能自动开启。新增 AvailableTargetIds 和 ResolveTarget：仅在 loader 线程按需双哈希/签名/ABI/RVA校验，不 patch、不调用业务、不提供游戏线程命令服务。显式功能名单仅Counter；独立诊断Counter/Trace精确签名、Trace默认无字段展开、有限速/总量/时限。诊断到期停止采集并尝试最终导出，但detour仍需重启/明确卸载才移除。保留全部原兼容性拒绝。源码与静态验证支持默认模式不增加目标方法调用路径成本，AI/渲染主动调用或显式诊断的性能不作保证。Release 零警告/错误，16+42=58 项托管/静态检查通过（含编译后默认早退/解析无patch检查）。构建包在 outputs/Hooks；本轮没有启动/关闭游戏，当前已运行进程未改动，本版本尚未游戏内验证和部署，未推送、未改tag。以下为历史迭代记录。

2026-09-30 AI 范围进一步收缩：Catalog.Load 在视觉策略后应用 AiHookPolicy，排除 Nivalis.GhostSystem.Ai 命名空间的直接观察入口及类型快照。Runtime AgentGhostSimulator 17 个、World AgentGhost 13 个，共 30 个；包括上次观察到约每秒 3.5 万次的三个热点。累计排除视觉 183 + AI 30 个，有效候选 Player 216、World 318、Item 210、Runtime 110，共 854。游戏原始 AI 不禁用，通用世界/经营观察保留。Release 零警告/错误，15+29=44 项静态检查通过；已在游戏关闭状态部署四插件和公共库，全部 DLL/PDB 哈希核对一致，未启动游戏。本次尚未游戏内或性能验证，修改未推送、标签未改动。

2026-09-30 后续范围调整：已按用户要求在 Catalog.Load 加入 VisualHookPolicy，安装前排除渲染/相机/动画/天气特效/光照/GUI/高亮及视觉展示入口；Player 108、World 19、Item 10、Runtime 46，共 183 个。保留 884 个候选（216/331/210/127），原始 JSON 不变，排除原因进入 Excluded，视觉类型和资源字段不参与快照。保留移动、交互、金钱、库存、时间、存档和 AI 业务。Release 0 警告/错误，15+28=43 项静态检查通过。已确认游戏关闭并部署四插件和公共运行库，DLL/PDB 复制后哈希一致；未改配置、未启动游戏。当前变更未推送，未新建/移动标签；本版本尚未游戏内验证，不能沿用旧版安装数或声称性能恢复。

**2026-09-30 崩溃修复与实测更新：已通过有限读档验证，交互复测待反馈。** 已按用户授权反复重启、分组隔离并部署修复版。当前 PID 36828，19:25:40 启动，四组 Enabled=true、所有 Groups=true、临时 ExcludedRvas 为空。最终安装 1019/1067，48 个明确拒绝；19:32 检查点共 403 个入口有真实回调，观察 Faults=0，进程有响应。19:38:13 复查时已运行 12 分钟，总计 405 个入口有命中，四组 Faults 仍为 0，日志未检出 [Error 或 Exception:。已正常进入原有市场存档；不能宣称所有入口或玩法均正常。自动按键未打开背包/暂停菜单，手动移动、背包与交互仍待用户反馈。

修复采用当前桥接版本的兼容性拒绝：32 个 ref/out、6 个投影值类型返回、6 个小结构体按值参数，以及单独复现 native 崩溃的 HoldableEntity.Update；另 3 个旧有签名匹配失败。拒绝方法仍走游戏原入口。HoldableEntity.Update 的短函数重定位原因仍是推断，未声称底层桥接缺陷已修复。两份调试 dump、逐次试验及生成 IL 已保留；无钩子对照组能进入同一存档。实际加载器自报 BepInEx 6.0.0-be.697/.NET 6.0.7，pre.2 只是历史目录名。Release 0 警告/错误，41 项静态检查通过。详见 [运行实测报告](HOOK-RUNTIME-TEST.md)，首次失败记录保留在 [原始报告](hook-test-20260930/RESULT.md)。

以下重构、发布和早期工具权限文字含历史状态；当前部署/实测状态以上述报告为准。下一步先完成手动交互反馈，再按用户指令推进功能，不应将被拒绝入口或未实施的改钱/加物品接口当成已验证。
四个观察钩子的外层重构已完成：PlayerHook、WorldHook、ItemHook、GameRuntimeHook 全部使用公共 HookRuntime。统一构建 [Build-Hooks.ps1](D:/NivalisNightsTrainer/Build-Hooks.ps1)，推荐交付 [outputs/Hooks](D:/NivalisNightsTrainer/outputs/Hooks)。四个插件 DLL 必须配套同一版 NightsHack.HookRuntime.dll；旧交付目录是兼容副本，不要把多个目录的重复插件同时部署。

上一轮已实际完成 Release 构建（0 警告/错误），15 项 Player 检查与 21 项跨插件/运行库检查通过，共 36 项。尚无修改器本体、命令执行通道、IPC 或游戏内验证。此次补充文档索引和根 README，并初始化版本控制、提交推送源码及开发标签；沿用上一轮构建结果，不重新编译，不部署。

用户已明确开发方向：目标是常规多功能修改器，不是 ModLoader，也不追求覆盖全部 Runtime 或引擎内部。钩子阶段以真实的游戏内部业务入口、正确签名及保留原逻辑为标准；具体实例、线程、校验、时机和持久化问题在功能实现时逐项处理。不能把“存在内部入口”升级为“游戏内已验证合法”。

按此标准，当前入口已具备开展修改器本体开发的基础，不应把继续全面扩钩设为前置任务。后续收到开发指令时，优先打通改钱、加物品、属性/经验、速度/传送：控制请求 → 游戏线程 → 有效实例 → 原业务方法 → 实际结果回传。当前文档更新指令本身不授权开发本体或部署游戏。

独立 MoneyHook 已删除；其调查报告与证据保留。未来改钱复用 PlayerInventory.set_Money，增加物品复用玩家/库存业务入口，不采用固定地址硬改临时数值。

## 四个钩子共用 HookRuntime（2026-09-30）

已按 PlayerHook 去重并实现三个观察插件：WorldHook 350、ItemHook 220、GameRuntimeHook 173 个静态候选；新增 743，配合原 PlayerHook 324 共 1,067 个互不重复的候选签名/原生 RVA。4 个重复物品入口继续只由 PlayerHook 观察。PlayerHook 已迁入 NightsHack.HookRuntime；四个插件统一使用 ObservationPlugin、观测记录/队列、签名校验和快照读取。公共库不另注册插件，每个插件均需要此依赖。

源码为 src/NightsHack.{PlayerHook,WorldHook,ItemHook,GameRuntimeHook,HookRuntime}；统一交付 outputs/Hooks；构建 ./Build-Hooks.ps1。旧 Build-PlayerHook.ps1 / Build-WorldHooks.ps1 转发到统一构建，旧输出目录同步刷新。Release 0 警告/错误，15 项 Player 检查与 21 项跨插件/运行库检查通过；没有部署、附加或游戏内 hook 验证。Player 外层/DLL 已重构，但 324 方法清单字节未变，属性/技能/锁/knowledge 快照保留。候选数不是实际安装数。

公共观测类型现在为 NightsHack.HookRuntime.HookObservation / HookObservationBuffer / HookStatus；旧 PlayerObservation 等显式类型不再保留，消费者需重新编译。Active/Observations/GetStatus/FieldDiagnostics/Installed 入口保留。每个插件独立队列/采样/状态，共用安装分发与去重。

ItemHook.BackpackItems 暴露 IBackpackItemAddition.AddToBackpackAsync：预留 RequestId、ItemTypeGuid、Quantity、结果/实际接受数量。当前 IsImplemented=false，有效请求返回 NotImplemented、AddedQuantity=0；不排队，不调用游戏逻辑。以后修改器需实现活实例解析、游戏线程执行、实际入库数量/部分成功、幂等和 IPC。

新档案 outputs/HOOKS-REFERENCE.md 包含四类职责、完整候选表、排除原因、观察限制与加物品接口。outputs/REFERENCE.md 继续存 Player。原始逆向 outputs/world-investigation.md 及 work/world-investigation 保留：71 类重点调查目录、202 条方法记录/194 个不同 RVA 的原生提取。新增清单更多方法仅为元数据确认，不应宣称已经逐条逆向或游戏内验证。

可再生清单：work/world-investigation/hook-scope.json → work/Generate-WorldHookCatalogs.ps1 → 各项目 Catalog.json；work/Update-WorldHooksReference.py 更新文档附录。hook-coverage-audit.json 记录 1,580 条范围内方法声明的候选/排除归属。新增共 837 条明确排除，包括共享体、构造/终结、抽象/泛型、普通访问器、编辑器/显式接口等。

关键结论：Item 权威数据在 InventorySystem；ItemStack 数量来自逐件实例列表；Ghost 注册入队不等于立即注册；GhostManagerSave.Save/Load 是空体，真实 Ghost 存取链在 SerializationManager。PlayerHook 也不是 Player 全部方法覆盖。

## 1. 关键事实与进度

- **已验证为 Unity 2020.3.44f1、Windows x64、IL2CPP，不是 UE5。** 不沿用早期 UE5 方案；UE4SS、Dumper-7 不适用于当前路线。
- 游戏目录：`D:/Steam/steamapps/common/Nivalis Nights`。主要输入为 `GameAssembly.dll` 与 `Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat`。
- 元数据版本 27；Il2CppDumper 自动选择内部解析版本 27.1 并成功导出。此数字不是 Unity 引擎版本。
- 已完成工具与环境验证。插件基线为 BepInEx 6.0.0-pre.2 Unity IL2CPP x64 / HarmonyX / net6.0；尚无修改器本体、加载器部署或游戏内 hook 验证。
- money 已静态追到 PlayerInventory._money 与 set_Money(int)，由 PlayerHook 统一观察；调查报告 outputs/money-investigation.md 保留。
- **PlayerHook 业务范围**：`src/NightsHack.PlayerHook`，推荐交付 `outputs/Hooks/PlayerHook.dll`，统一入口 `Build-Hooks.ps1`。18 组、324 个候选方法、32 个字段 schema（338 字段），46 个明确排除条目；Release 0 警告/错误，15 项托管/静态检查通过。仅观察，无写值/修改器本体/部署/游戏内命中验证。结构、逻辑和完整清单归档在 `outputs/REFERENCE.md`；底层调查在 `outputs/player-investigation.md`。
- 用户已授权 PID 40208 的附加及读写，但本次 WinDbg attach_process / IL2CPP list_dumps 被工具审批策略拒绝（approval policy is never）；此次未附加，状态查询确认无会话。不要将历史成功附加与本次拒绝混淆，也不要绕过工具策略。
- “无反作弊”由用户提供，未全面独立验证。历史 PID 40208 已成功附加、读取内存、枚举模块、反汇编并解除附加；新会话必须重新确认 PID，不复用历史绝对地址。

## 2. 架构方向与实现边界

`未来 C# 修改器界面/控制层 → 待实现的 IPC/游戏线程命令层 → PlayerHook / WorldHook / ItemHook / GameRuntimeHook（共用 HookRuntime） → 动态解析的游戏业务入口`

当前只有观察钩子与加物品接口占位实现。HookRuntime 负责四者公共安装/卸载、版本/签名校验、回调分发、采样、快照和状态；它不额外注册为游戏插件，也不替代待实现的命令层。

- 优先 C#；游戏内目标框架必须匹配实际加载器，不因本机装了 SDK 10 就默认能向游戏加载 net10.0。
- 以程序集、命名空间、类名、方法名和完整参数签名识别方法，处理重载。能调用原方法就调用，需要拦截时再 hook。
- **不以硬编码“模块基址 + 固定偏移”写 runtime 数值作为主要机制。** 地址应是运行时解析结果；RVA 用于核对与记录。
- 无法按类方法定位时，用字符串及交叉引用、调用链等线索找回可执行 hook 点；字符串本身不是可执行 hook 点。
- 名称和字符串也会变；更新后验证版本、签名、定位唯一性和行为。定位失效或有歧义时停用，不猜地址。
- 区分 UI 显示、权威数据、交易/存档逻辑；调用前验证实例、参数、线程及副作用。

## 3. 工具与证据入口

| 项目 | 位置 / 状态 |
|---|---|
| 实际游戏导出 | `D:/NivalisNightsTrainer/work/il2cpp-validation/`：script.json、dump.cs、il2cpp.h、stringliteral.json、DummyDll |
| MCP 原始结果 | 同目录 `mcp-results.jsonl`、`mcp.stderr.log` |
| IL2CPP MCP | `C:/Users/Administrator/.codex/mcp-servers/il2cpp-decompiler/bin/il2cpp_mcp.exe`，握手版本 1.1.0 |
| 配套导出工具 | 同一 bin 下 `Il2CppDumper/Il2CppDumper.exe` |
| IL2CPP 包内 skill | `C:/Users/Administrator/.codex/mcp-servers/il2cpp-decompiler/skills/il2cpp-decompiler/SKILL.md` |
| WinDbg MCP | Native 版已对真实进程验证基础通路；使用 session_id，检查状态，结束自己的会话并确认解除附加 |
| Ghidra MCP | 已有接口；尚未对本游戏执行导入/反编译验证 |

MCP 顺序：`list_dumps → load_project → search_symbols → get_class_info → decompile_method`。AppData 的 list_dumps 为空不代表工作区无映射；load_project 直接传游戏 GameAssembly.dll 和工作区 script.json 的绝对路径。

早期会话原生工具目录未暴露新 MCP，曾直接启动上述 exe，通过 **stdio 换行 JSON-RPC** 握手与调用成功。本次已发现 IL2CPP、WinDbg、Ghidra 接口，但 IL2CPP 查询和 WinDbg 附加曾被审批策略拒绝；接口未暴露与审批拒绝是两种不同情况，不能用直接协议绕过审批拒绝。会话结束后工具权限可能变化，新会话应检查实际返回，不把历史策略当作永久状态。不要重跑会生成合成样本的 verify_local.py，也不要把合成样本当作游戏验证。

**已验证与局限：**

- MCP 加载 243,784 条方法记录、29,761 个类索引项及字段布局；记录含泛型实例/共享地址，不等于独立函数数量。
- `Nivalis.InventorySystem.FoodItemType.get_ConsumeTime`：RVA `0x2ED31B0`，机器码 `8B 81 60 01 00 00 C3`，读取 `this+0x160` 的 int，与元数据和伪代码一致。仅是已验证样本，不是固定地址方案。
- `Nivalis.PlayerMoneyDisplayUI.UpdateMoney` 可反编译，但实际两个 int 参数被输出成 object，并残留未声明变量。**复杂伪代码不能直接信任或当可编译源码。** 该方法属于显示层，尚未证明能修改实际余额。
- DummyDll 为元数据辅助产物，不是完整恢复的游戏实现。
- 输入 SHA-256 和更多证据见 `outputs/il2cpp-validation.md`；游戏更新后先核对，不盲用旧映射。

## 4. C# 环境

- 工作区 `.tools/dotnet` 已装 x64 SDK **10.0.401**，运行库 **10.0.12**，安装包官方 SHA-512 校验通过。未永久修改系统 PATH。
- 推荐从工作区调用封装，避免用户 NuGet 目录权限问题：

```powershell
.\Invoke-Dotnet.ps1 --info
.\Invoke-Dotnet.ps1 build .\work\csharp-validation\CSharpValidation.csproj -c Release
```

- `Invoke-Dotnet.ps1` 使用 `Enter-CSharpEnv.ps1`，临时将 APPDATA/LOCALAPPDATA 指向工作区并在结束后恢复；缓存位于 work。
- C# Release 编译 0 警告/错误，x64 执行、WinForms 句柄创建及程序化事件验证通过；不代表可见 UI、WPF 或游戏 hook 已测试。测试项目的 NuGet.Config 清空包源，仅用于离线环境验证，不作为正式项目的包源策略。
- 详情：`outputs/csharp-environment.md`。

## 5. 金钱能力归并到 PlayerHook

独立 MoneyHook 的核心余额观察已由 PlayerHook 承接，旧插件文件已清理。无需恢复旧项目即可继续开发改钱功能。

- 已包含 PlayerInventory.set_Money(int)、_money 前后快照及请求参数；setter 不按时间采样。旧值/请求值/实际值分别对应同一 CallId 的 Before 字段、Before 参数、After 字段；API 与旧 MoneyObservation 不同。
- 后续改钱统一在 PlayerHook 中增加受控命令：解析当前有效玩家库存，在游戏线程调用原 setter；通信、实例归属和实际效果仍需实现/验证。现有快照地址不是可直接写入的永久句柄。
- 保留 outputs/money-investigation.md、work/money-investigation/ 和调查脚本。报告里的外围经济系统未全部钩住，旧 MoneyHook 也没有这些能力。
- 统一构建入口为 ./Build-Hooks.ps1，Build-PlayerHook.ps1 为兼容转发入口；共享依赖 work/hook-dependencies/bepinex-6.0.0-pre.2/BepInEx/core 保留。

## 6. 项目规则

1. 真实证据优先。区分已验证、推断、未验证；不编造 API、文件、执行结果和兼容性。编译成功不等于游戏内有效。
2. 先检查实际源码/配置/版本，再做最小必要变更；不为省事更改依赖版本或架构。实验易回退，避免无关修改。
3. 尊重每轮授权。“只读、不动代码、不新增文件”优先；先前允许工作区写入不等于以后可任意改游戏、注入或改内存。已明确授权的正常步骤自主推进，不反复问同一权限。
4. 开发与中间产物放工作区，临时分析放 work，用户交付放 outputs；游戏目录默认只读。
5. 反编译必须交叉核对元数据、机器码和必要的运行证据；发现冲突如实报告。大型映射定向检索，勿整份输出。
6. 验证完成后清理自己的调试会话；不终止用户游戏或其他会话。每个功能说明定位依据、方法职责、调用/hook 方式、验证证据和失效检测。

## 7. 新对话提示词（可直接复制）

> 请读取 D:/NivalisNightsTrainer/outputs/HANDOFF.MD、REFERENCE.md 和 HOOKS-REFERENCE.md 接手。PlayerHook/WorldHook/ItemHook/GameRuntimeHook 已全部共用 HookRuntime，候选分别 324/350/220/173，共 1,067 个不重复入口。统一构建 Build-Hooks.ps1，交付 outputs/Hooks；四插件加一份公共 DLL。Release 与 36 项托管/静态检查已通过，未部署、未验证游戏内效果。加物品接口只是占位，有效请求返回 NotImplemented，不会执行。按本轮指令推进：项目目标是调用正常游戏内部业务的多功能修改器，不要为“更底层”继续全面研究引擎或扩钩。当前没有本体、IPC 或命令执行；这些应在用户要求开发时实现。不要复用历史 PID、固定地址或旧 DLL 哈希，不把候选数、编译结果当成原生成功命中。工具审批拒绝必须尊重，阅读交接不等于部署授权。

建议首批功能：改钱、加物品、属性/经验、速度/传送。按功能验证入口、实例归属、游戏线程和真实结果；再做 UI/存档一致性检查。不以读完整个 Runtime 源码作为前提。

## 8. PlayerHook 与 Player 档案（2026-09-30）

- PlayerHook 最初按观察型插件实现，现已迁入公共 HookRuntime；仍不主动调用游戏业务、不写值。
- 插件类 `NightsHack.PlayerHook.PlayerHook`，ID `nightshack.playerhook`；沿用既定 BepInEx IL2CPP/net6.0/x64 依赖。`PlayerHook.Active` 提供只读快照队列、方法状态和字段诊断；外部 UI 需要后续独立 IPC。
- 18 组涵盖生命周期、Player 业务、属性、角色、移动、交互、焦点、持物、摆放、动画、相机、环境、导航、库存、技能、Ghost、存档、输入。候选数不是原生安装/命中数。
- 校验两个游戏输入 SHA256、完整签名、动态 MethodInfo 原生入口对应本版 RVA；共享地址/泛型/静态入口明确排除。运行只用生成的 interop，不用 DummyDll。
- 记录 Before/After、CallId、参数/结果与实例字段；IL2CPP 字段读取，字符串限长，引用仅诊断地址。属性/技能字典尝试只读枚举最多 32 项，失败记录 unavailable；不能当成空字典或默认值。
- 512 条有界队列、最新值按方法+阶段保留；高频候选默认 250 ms 采样，跨实例；未筛选 LocalPlayer。Calls/样本/Faults 与字段诊断各自区分。
- `PlayerInventory.set_Money` 已包含。`CancelOrder/PrePlayerTravel`、部分简单 setter/getter 共享 native body，未直接钩；协程只观察入口，不代表完成；全部逻辑和排除边界见 REFERENCE。
- 构建：`./Build-Hooks.ps1`（旧入口兼容转发）。15 + 21 = 36 项检查为托管/静态，不是原生集成测试。初次在线漏洞元数据查询 NU1900 失败，后续使用缓存和单次 `'-p:NuGetAudit=false'` 还原；没有改依赖版本，未完成在线漏洞审计。
- 清单生成：`work/Generate-PlayerCatalog.ps1`；附录更新：`work/Update-PlayerReference.ps1`。清单有变更时先复查分类，再更新测试/文档，不盲目扩大原生钩子。
- 统一交付 outputs/Hooks 有四插件 DLL/PDB、公共 HookRuntime DLL/PDB、README、两份参考文档、各插件 Catalog 和 build-sha256.json；旧 outputs/PlayerHook、outputs/WorldHooks 同步刷新，以实际文件和新哈希为准。
- **尚未验证**：实际 loader 兼容、324 候选可安装数量、原生回调、生成的字典投影、各 schema 可读性、性能、存档往返、卸载。未写游戏目录。
- Player 底层报告来自已有导出/只读 PE：75 份方法反汇编记录、74 个唯一入口；IL2CPP MCP 因审批后端 403 无法执行，未绕过审批。详细工具限制、输入身份、字段与公式见 player-investigation.md。



## v1.1 Catalog 修复

- 重新生成四个 Catalog：Player 323、World 349、Item 220、Runtime 174，共 1,066 个入口，RVA 全部唯一。
- 生成器、静态检查和文档统一使用 work/il2cpp-validation-v1.1；已移除对旧 disassembly-index.json 的构建检查依赖。
- 44 项 WorldHooks 静态检查和四插件编译通过；未执行游戏内注入或玩法验证。