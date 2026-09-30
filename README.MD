# Nivalis Nights Modifier

**NightsHack · v0.1-Dev** — 面向 Nivalis Nights 的 C# 多功能修改器项目。

当前已完成四个观察钩子及公共运行库，下一阶段开发修改器本体。目标是通过游戏自身业务入口实现功能；不以固定地址修改临时数值为主要方案，不追求完整重建游戏 Runtime 或引擎。

## 当前状态

- 四个插件共用 `NightsHack.HookRuntime`，合计 **1,067 个静态候选入口**。
- 本地 Release 构建通过，零警告、零错误；**36 项托管/静态检查通过**。
- 尚未部署或验证游戏内运行；候选数不等于实际安装或命中数。
- 尚无修改器 UI、IPC 或命令执行层。加物品接口仅预留，当前不会实际添加物品。

## 模块

| 模块 | 职责 | 候选入口 |
|---|---|---:|
| PlayerHook | 玩家属性、技能、金钱、移动、交互与背包玩家入口 | 324 |
| WorldHook | 场景旅行、世界实体、空间索引、任务对象和地产经营 | 350 |
| ItemHook | 库存、堆栈、逐件物品、腐坏、拾取摆放和家具 | 220 |
| GameRuntimeHook | 时间、模拟、天气、存档和对象池 | 173 |
| HookRuntime | 四插件公共安装、校验、观察回调、采样、快照与卸载 | 公共依赖 |

首批规划功能包括改钱、增加背包物品、调整属性/经验、速度及传送；已有相关业务入口，功能本身仍待实现与实测。

## 开发与构建

目标环境：Windows x64、Unity 2020.3.44f1 / IL2CPP、插件 `net6.0`、BepInEx 6.0.0-pre.2、Il2CppInterop.Runtime 1.4.6、HarmonyX 2.10.2。检查项目使用 `net10.0`。

当前构建脚本沿用本地开发环境约定，仓库不是开箱即用的完整 SDK 包：

1. 在 `.tools/dotnet` 准备 .NET SDK（已验证版本 10.0.401）及构建所需引用包。
2. 在 `work/hook-dependencies/bepinex-6.0.0-pre.2/BepInEx/core` 准备上述版本的 BepInEx/Interop/Harmony 和 Mono.Cecil 依赖。
3. 完整静态检查需要从自己的游戏副本生成 `work/il2cpp-validation` 下的元数据辅助文件，以及对应的 `work/world-investigation/disassembly-index.json`。调查及生成脚本在 `work` 中；详细前置条件见参考文档。
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
