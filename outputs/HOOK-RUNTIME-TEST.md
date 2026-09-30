# 2026-09-30 钩子崩溃排查与运行验证

本报告记录实际执行过的测试，替代首次失败报告作为当前状态入口；原始失败证据保留在 `hook-test-20260930/RESULT.md`。时间均为本机 UTC+08:00。

## 当前验证边界

- 最终修复版试验 `17-final-compatibility-guards`，PID 36828，19:25:40 启动，已通过“继续”进入原有市场存档。19:38:13 复查时进程仍在运行且有响应（启动后 12 分钟），BepInEx 日志未检出 [Error 或 Exception:；五个部署 DLL 的 SHA256 与本次构建产物一致。最终快照见 final-checkpoint.json / final-process.json。
- 四插件全部 Enabled=true，所有 Groups=true，临时 ExcludedRvas 为空；兼容性拒绝在代码中执行，不依赖本机排除配置。
- 安装 1,019/1,067 个候选；48 个明确 Rejected，没有 Failed 或配置 Disabled。拒绝安装意味着该方法继续执行游戏原入口，不是该方法的钩子已修复。
- 19:32 的诊断检查点：GameRuntimeHook 90、ItemHook 66、PlayerHook 110、WorldHook 137 个入口有真实回调，共 403 个；四组观察 Faults 均为 0。19:38 复查时 PlayerHook 增至 112，总计 405 个，四组观察 Faults 仍为 0。这个计数不是全部已安装入口的完整业务验证。
- 静态验证：四插件 Release 编译 0 警告/错误；15 + 26 = 41 项托管/静态检查通过。静态检查不证明原生 ABI 或游戏玩法正确。
- 手动移动、背包、NPC/物品交互复测仍待用户反馈。自动按键未使背包/暂停菜单出现，不能据此声称交互正常。性能、完整字段覆盖、长期运行、写入功能及热卸载尚未完整验证。

| 插件 | 候选 | 安装 | 拒绝 | 检查点已观察 |
|---|---:|---:|---:|---:|
| PlayerHook | 324 | 315 | 9 | 110 |
| WorldHook | 350 | 334 | 16 | 137 |
| ItemHook | 220 | 205 | 15 | 66 |
| GameRuntimeHook | 173 | 165 | 8 | 90 |

## 已确认的故障与处理

1. `SerializationManager.LoadSaveHeader`：运行时组二分中，单独排除该入口允许启动；恢复该入口会崩溃。已读取本机生成 IL：原生 SaveHeader 是结构体，Interop 投影为类，out 包装却使用指针大小临时存储。现拒绝全部 ref/out 方法，共 32 个，包括后续实际报错的 `ApplyVolumes(ref float)` 与 `GetClosestVenue(..., out float)`。未逐一证明 32 个入口都会崩溃；这是针对当前桥接实现的保守兼容性边界。
2. `GetValidSaves`：日志确认生成方法发生 InvalidProgramException。拒绝 6 个被投影为托管类的原生值类型返回值入口；其他返回同类的方法未逐个复现。
3. 小结构体按值参数：调试器捕获原生复制 8 字节时，将 `0x00052aca00052aca` 当地址读取的 AV。已检查实际安装 HarmonySupport 的 IL：x64 路径将这类参数视为指针后装箱。拒绝 6 个投影为类且原生大小为 1/2/4/8 字节的按值参数入口。
4. `HoldableEntity.Update`，RVA `0x840710`：完成前述 ABI 防护后仍读档崩溃。无钩子和运行时单组都能读档；运行时+物品组会崩溃；四组只排除 HoldableEntity 可以读档；进一步只排除 Update 也能读档。该方法本机原生体只有 14 字节，包含短条件跳转及尾跳转。**已验证**兼容性隔离有效；**推断**与当前 detour 的短函数重定位有关，尚未完全解码并证明底层缺陷。代码仅在双哈希版本锁及完整方法/RVA 匹配下拒绝该入口，保留其余 HoldableEntity 方法。
5. 另有 3 个 PlayerHandsAnimator 编译器生成方法的签名无法唯一匹配，仍拒绝安装。这是先前已有诊断，不是本次新隔离。

合计拒绝：32 ref/out + 6 值类型返回 + 6 小结构体参数 + 1 已复现 native 入口 + 3 签名不匹配 = 48。完整签名及原因见 `hook-test-20260930/17-final-compatibility-guards/rejected.csv`。候选 catalog 未删改，仍为 1,067。

## 主要对照试验

| 试验 | 配置/目的 | 实际结果 |
|---|---|---|
| 首次 PID 36208 | 四插件旧版 | 启动后 coreclr AV，未通过 |
| 01 | 仅运行时，关闭诊断导出 | 仍崩溃，导出不是必要触发条件 |
| 02–07 | 序列化组二分 | 缩小到 LoadSaveHeader；只排除该入口可启动 |
| 08 | 拒绝投影结构体 ref/out | 读档报 GetValidSaves InvalidProgramException，仍失败 |
| 09–10 | 加返回值及小结构体参数防护 | 仍失败；捕获原生 AV 和 ref float trampoline 异常 |
| 11 | 拒绝所有 ref/out | ref/out 错误消失，但读档仍有 native 崩溃 |
| 12，PID 41600 | 四插件 Enabled=false，加载器仍在 | 同一存档成功进入市场场景 |
| 13，PID 42392 | 运行时+物品 | 读档崩溃 |
| 14，PID 34048 | 仅运行时 | 成功进入市场，回调持续 |
| 15，PID 48544 | 四组，排除 HoldableEntity | 成功进入市场 |
| 16，PID 57576 | 四组，仅排除 HoldableEntity.Update | 成功进入市场并持续运行 |
| 17，PID 36828 | 最终代码防护，清空临时排除 | 成功进入市场；检查点状态见上文 |

不要把各试验 `result.json` 的启动后 20–45 秒存活结果等同于读档通过。读档证据另存 `loaded*`、`load-attempt.log` 与诊断 JSON。

## 环境、部署与证据

- Unity 2020.3.44f1 IL2CPP x64；实际加载器自报 BepInEx **6.0.0-be.697+53625800b86f6c68751445248260edf0b27a71c2**，.NET 6.0.7；Interop 1.4.6 / HarmonyX 2.10.2。`work/hook-dependencies/bepinex-6.0.0-pre.2` 是历史目录名，不能用它替代实际二进制版本。此次未升级依赖。
- 游戏目录 `D:/Steam/steamapps/common/Nivalis Nights`，插件在 `BepInEx/plugins/NightsHack`；四 DLL 与同版 `NightsHack.HookRuntime.dll` 一起部署。原游戏程序集与 metadata 双哈希仍在加载前校验。
- `work/Run-HookTrial.ps1` 为本机复测试验脚本，正常请求关闭游戏，不强杀，失败即停止；每次必须使用新试验目录名。不要重跑硬编码首次旧 PID 的 `work/Deploy-HookTest.ps1`。
- 临时诊断配置：`[Diagnostics] ExportIntervalSeconds=5` 将脱离原生对象的状态和 Latest 导出到 `BepInEx/diagnostics`；默认 0 关闭。后台定时器不读取游戏原生对象。`ExcludedRvas` 是二分工具，最终配置为空。
- 诊断 Faults 仅统计观察代码捕获的错误；桥接器先于回调发生的错误、原生 AV 或游戏原方法异常不一定计入，不能只看 Faults=0。仍有 7 项不可读取字段的诊断，未宣称字段全部覆盖。
- 本轮保留两份 WinDbg dump、栈、生成 IL 和逐次日志于 `outputs/hook-test-20260930/`；曾启动的两个调试器会话均已结束。没有留下暂停的调试进程。
- 仍只有观察能力；增加物品接口是 NotImplemented，修改器 UI、IPC、游戏线程命令、改钱/加物品等没有在本次实现或验证。
