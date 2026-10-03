# Nivalis Nights Modifier

2026-09-30 最新 v0.4.2-Dev：界面只保留“手动注入”和注入器日志。删除游戏路径/浏览区域、两句说明、实时钩子日志页及其轮询定时器；按运行中 Nivalis Nights 进程定位 PID，后台读取进程路径完成原有校验和部署，拒绝无目标/多个目标并校验 PID+启动时间避免换进程。既有钩子日志输出和审计链保持不变。成品 outputs/BuildRelease/v0.4.2-Dev；构建与 12 项修改器检查通过，UI 已渲染核对，本轮没有重启游戏或重新注入。下方双页日志说明为历史记录。

2026-09-30 修改器 v0.4.1-Dev：标题改为 Kainy's Nivalis Nights Trainer，彻底移除静态注入按钮/API/启动文件，仅动态注入。新增工程化注入日志、按进程会话实时读取的钩子日志及未来功能审计包装。成品 outputs/BuildRelease/v0.4.1-Dev/NightsHack.exe；保留整个目录。58+11 项检查通过，实际动态加载四插件及 GUI 显示当前进程日志已验证；玩法、长期性能和未来功能仍需独立验证。详见 [修改器说明](src/NightsHack.Modifier/README.md)。

2026-09-30 减负改造：四插件默认只登记 1,067 个被动入口，观察安装为 0。AI/渲染资料已恢复，但不会自动 hook 或采集；只有功能精确允许列表或独立启用的诊断才安装观察补丁。新增按需 ResolveTarget 解析接口，不执行游戏业务。旧 Enabled/Groups/Export 配置不能恢复全量观察。构建检查通过，本轮未启动游戏；详情见 src/NightsHack.HookRuntime/README.md。


**NightsHack · v0.1-Dev** — 面向 Nivalis Nights 的 C# 多功能修改器项目。

当前已完成四个观察钩子及公共运行库，下一阶段开发修改器本体。目标是通过游戏自身业务入口实现功能；不以固定地址修改临时数值为主要方案，不追求完整重建游戏 Runtime 或引擎。

## 当前状态

- 四插件共用 HookRuntime，1,066 个 v1.1 被动目录入口；默认 0 个观察补丁。
- Release 构建零警告、零错误；60 项托管/静态检查通过，含默认早退、按需解析、独立诊断及预算。
- 调整前版本已部署并完成读档崩溃隔离：1,019 个候选安装，48 个明确拒绝，四组已有真实回调；完整玩法和长期稳定性尚未全面验证。详见 [运行实测报告](outputs/HOOK-RUNTIME-TEST.md)。候选数不等于实际安装或命中数。
- 已有仅动态注入 UI、功能审计包装和 PlayerHook 的本地 IPC/游戏线程命令执行层；当前支持玩家数据探测和余额设置。加物品接口仅预留，当前不会实际添加物品。

## 模块

| 模块 | 职责 | 候选入口 |
|---|---|---:|
| PlayerHook | 玩家业务、移动/交互、相机与动画入口资料 | 323 |
| WorldHook | 场景、实体、AI Agent、任务与经营入口资料 | 349 |
| ItemHook | 库存、物品、摆放与展示入口资料 | 220 |
| GameRuntimeHook | 时间、AI、天气光照、存档和对象池入口资料 | 174 |
| HookRuntime | 四插件公共安装、校验、观察回调、采样、快照与卸载 | 公共依赖 |

首批规划功能包括改钱、增加背包物品、调整属性/经验、速度及传送；改钱已接入 PlayerHook，其他功能仍待实现与实测。

## 开发与构建

目标环境：Windows x64、Unity 2020.3.44f1 / IL2CPP、插件 `net6.0`、Il2CppInterop.Runtime 1.4.6、HarmonyX 2.10.2。实际部署的 BepInEx 自报 6.0.0-be.697 / .NET 6.0.7；依赖目录的 pre.2 名称是历史命名。检查项目使用 `net10.0`。

当前构建脚本沿用本地开发环境约定，仓库不是开箱即用的完整 SDK 包：

1. 在 `.tools/dotnet` 准备 .NET SDK（已验证版本 10.0.401）及构建所需引用包。
2. 在 `work/hook-dependencies/bepinex-6.0.0-pre.2/BepInEx/core` 准备上述版本的 BepInEx/Interop/Harmony 和 Mono.Cecil 依赖。
3. 完整静态检查需要从自己的游戏副本生成 `work/il2cpp-validation-v1.1` 下的元数据辅助文件，以及对应的当前 `work/world-investigation/hook-scope.json` 和覆盖审计。调查及生成脚本在 `work` 中；详细前置条件见参考文档。
4. 前置条件齐备后运行：

```powershell
./Build-Hooks.ps1 -Restore
# 已还原依赖时：
./Build-Hooks.ps1
```

统一本地产物位于 `outputs/Hooks`：四个插件 DLL 和一份公共 `NightsHack.HookRuntime.dll`。旧 `Build-PlayerHook.ps1`、`Build-WorldHooks.ps1` 转发到统一构建。构建脚本不会部署游戏。

仓库包含源码、钩子目录、检查程序、项目文档和分析脚本；不包含游戏二进制、完整反编译导出、第三方依赖、SDK、缓存或编译产物。当前钩子有游戏版本哈希校验，不应视为对任意游戏版本兼容。

## 文档

- [文档目录索引](outputs/catalog.md)：两份参考文档的全部章节导航，不含详细方法。
- [项目交接](outputs/HANDOFF.MD)：当前进度、约束及后续工作。
- [项目入口与 Player 参考](outputs/REFERENCE.md)：业务入口、Player 结构和实现边界。
- [四钩子与公共运行库参考](outputs/HOOKS-REFERENCE.md)：职责划分、公共 API、完整候选表及排除项。
- [World 调查](outputs/world-investigation.md) · [Player 调查](outputs/player-investigation.md) · [金钱调查](outputs/money-investigation.md)。

部分调查文档链接指向本地证据路径；对应游戏导出不随仓库发布。`v0.1-Dev` 标记观察基础设施开发快照，不是已可用的修改器发行版。

## 开源协议

本项目采用 [GNU General Public License v3.0](LICENSE)（SPDX：`GPL-3.0-only`）。