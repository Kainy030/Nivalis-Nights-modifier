# HookRuntime：被动目录、按需解析与独立诊断

更新：2026-09-30。四插件仍共用 NightsHack.HookRuntime.dll。默认只登记业务入口，不安装观察补丁，不初始化原生字段快照，不启动诊断导出定时器。正常运行时没有本库附加到原版方法上的持续回调。

## 目录与安装分离

| 插件 | 被动目录入口数 | 默认观察补丁数 |
|---|---:|---:|
| PlayerHook | 324 | 0 |
| WorldHook | 350 | 0 |
| ItemHook | 220 | 0 |
| GameRuntimeHook | 173 | 0 |

原先因性能排除的 183 个视觉入口、30 个 AI 入口已恢复到被动目录。目录项存在不等于安装、调用安全或经过游戏内验证。共享 RVA、签名歧义等原始排除项仍保留。仅解析少量托管目录会有一次性启动/内存成本，不会因原版持续执行该方法而触发观察工作。

AvailableTargetIds 列举完整签名。Active 非空表示插件登记可用；Installed=false 在默认模式是预期结果。GetStatus 返回实际选中的观察项和原始排除记录，不把全部目录假装成已安装。

ResolveTarget(targetId) 是按需解析 API：要求插件已加载且在其 loader 线程调用，首次使用才加载原生上下文并检查游戏双哈希，再验证类型、完整签名、ABI 和 RVA，返回 MethodInfo。它不安装补丁、不执行方法、不获取活实例、不提供自动游戏线程调度。loader 线程约束并不证明任何玩法调用时机都安全。未来命令层必须另行验证对象、线程、状态、参数和实际结果。

## 默认配置与明确启用

每个插件独立配置。旧 Hook.Enabled=true、Groups=true、ExportIntervalSeconds=5 都不会自行恢复全量观察。

```ini
[Hook]
Enabled = true
[Features]
AllowList =
[Diagnostics]
Enabled = false
Mode = Counter
AllowList =
ExportIntervalSeconds = 0
```

Features.AllowList 仅接受分号或换行分隔的完整方法 ID，默认空；明确选择的功能观察目前仅计数（Counter），不自动实现任何修改器功能。Diagnostics 必须另外 Enabled=true，并明确指定 Counter 或 Trace 及 AllowList。签名参数含逗号，不能使用逗号拆分入口。不支持通配、整个组或模糊名称；未知 ID/非法启用模式导致安装拒绝。诊断关闭时忽略残留诊断模式和列表。

例：Nivalis.PlayerCharacter.Initialize() 是精确 ID 的格式示例，不是推荐常驻观察的目标。启用高频 AI/渲染诊断仍会产生桥接成本；恢复目录并不表示这些高频观察已经免费。

## Counter 与 Trace

- Counter 只装一个不请求实例/参数/返回值的 Prefix，计数而不读取快照。已选方法仍经过桥接，因此只在必要时选择。
- Trace 才使用 Before/After；所有选中入口都执行采样间隔，不再依赖旧 Sampled 名称分类。
- Observation.SampleIntervalMs 默认 250，范围 25..10000；同一方法跨实例共享门限，不代表完整世界状态。
- Diagnostics.CaptureFields 默认 false；默认 Trace 仅描述参数/返回值。CapturePlayerDetails 默认 false，且须同时允许字段读取才展开玩家属性、技能和锁。
- MaxSamplesPerSecond 默认 20（1..200）、MaxTotalSamples 默认 500（1..5000），每插件共享预算在字段读取/载荷分配前准入；一次额度对应一组 Before/After，通常是两条记录。
- DurationSeconds 默认 30（1..300），诊断时限从补丁安装完成起算。时限结束停止新计数/采集；已开始的调用可完成配对的 After。Trace 另有首个样本起算的预算时限，实际受较早截止限制。
- 时限到期不自动卸载 detour；桥接仍保留至游戏退出或明确卸载。要回到默认无观察开销状态，应关闭选择并重启；不承诺热卸载已验证。
- ExportIntervalSeconds 默认 0，正值最少 5 秒，必须明确启用诊断且实际安装成功才创建 Timer。到期尝试导出最终状态后停止 Timer；短于导出间隔的会话也会安排终态导出。写出失败记录警告，不无限续期。
- 后台只格式化/导出脱离游戏对象的托管快照，不从后台读取原生对象。旧 diagnostics JSON 可能来自旧 PID，应检查 ProcessId/TimestampUtc。

## 安全边界与验证

保留 ref/out、投影原生值类型返回、小结构体按值参数等兼容性拒绝。HoldableEntity.Update 的已复现 detour 不兼容仍在观察安装前拒绝；恢复目录没有绕开这些保护。按需解析没有证明该方法适合任意游戏状态或参数。

修改器 UI、IPC、主线程命令服务仍未实现。ItemHook.BackpackItems.IsImplemented=false，有效请求返回 NotImplemented；本次不是改钱/加物品功能交付。

四插件 Release 构建零警告/错误，16+42=58 项托管/静态检查通过，由 Build-Hooks.ps1 执行；检查包括默认零选择、旧配置隔离、精确签名、预算并发、Counter 无采集、ResolveTarget 无 patch/invoke、空选择在原生初始化前返回。编译及静态检查不代表游戏内性能实测。

统一包：outputs/Hooks；旧 PlayerHook/WorldHooks 目录为兼容副本。四 DLL 必须配同版公共库。构建脚本不会部署或启动游戏。本轮没有启动游戏，没有帧时间基准测试；旧读档实测属于之前的广覆盖观察版本。
