# Nivalis Nights：Money 核心追踪

日期：2026-09-30。工作区：D:/NightsHack。

## 执行状态与限制

- 用户授权附加 PID 40208，并允许读取、写入。Get-Process 确认该 PID 的 ProcessName 为 Nivalis Nights；进程路径未从这次查询成功取得。
- WinDbg attach_process 与 IL2CPP list_dumps 均返回 `MCP tool call requires approval, but approval policy is never`。没有成功附加，没有读取或写入游戏进程内存，没有注入或修改游戏文件。
- 附加尝试前后 session_status 均确认无调试会话；无需解除本轮会话。
- 以下是现有导出元数据与当前磁盘机器码的静态分析，不是运行时验证。没有绕过被拒绝的接口。
- 当前 GameAssembly.dll SHA256：9A0E32C2D09A5025F867D29BF39B9BEDD0715B513456617FBFD82C581E1A376D。
- 当前 global-metadata.dat SHA256：C8BD44F74B47136AEAD259DC2B88F289C12CB01E083FEEECDCD096A6FC1B2CF9。
- 两个哈希均与交接验证记录一致；这验证磁盘输入身份，不证明当前已加载模块未被修改。

## 1. 是否找到核心软件包

**已验证（元数据映射）：money 核心类属于逻辑程序集 Assembly-CSharp.dll。** dump.cs 第 98 行声明该 image 从 TypeDefIndex 11635 开始，且为最后一个 image；PlayerInventory 的 TypeDefIndex 为 13465，EconomyManager 为 14164。这些类在对应脚本映射中具有 GameAssembly.dll 的原生方法 RVA。

这指向游戏自身业务程序集，而不是一个已证实独立安装的第三方“金钱 SDK”。Assembly-CSharp.dll 是逻辑程序集名称；实际 IL2CPP 方法机器码在 GameAssembly.dll 内。DummyDll 中的同名文件只是元数据辅助产物，不能把它当作完整游戏实现。

从余额向外围追踪，核心涉及 Nivalis.InventorySystem、Nivalis.Player、Nivalis.Economy、Nivalis.Dialogue、Nivalis.GhostSystem.CustomerLoop，以及 Nivalis 下的 PlayerManager、RentManager、SerializationManager。尚未证明存在一个包揽所有经济逻辑的单一管理器。

## 2. 最底层已确认的余额存储与对象关系

元数据字段及已读取 getter 机器码一致：

| 对象 | 字段 | 本版本对象内偏移 | 证据 |
|---|---|---|---|
| Nivalis.PlayerManager | _localPlayer：PlayerManager.Player | 0x18 | getter：48 8B 41 18 C3 |
| PlayerManager.Player | inventory：PlayerInventory | 0x78 | getter：48 8B 41 78 C3 |
| PlayerInventory | _money：int | 0x38 | getter：8B 41 38 C3 |
| PlayerInventory | onMoneyChanged | 0x18 | 元数据；setter 通知路径与此偏移相符 |
| PlayerInventory | lockedMoneyEventPreviousValue：Nullable<int> | 0x28 | 元数据；setter 保存旧值的路径相符 |
| PlayerInventory | moneyChangeEventLock | 0x30 | 元数据；setter 从此处读取锁对象 |
| PlayerManager.Player | personalReceipts：ReceiptList | 0x80 | 元数据；带收据入口存在读取 +0x80 的路径 |
| PlayerManager.PlayerSave | Money：int | 0x18 | 元数据中的存档字段，非活动余额实例 |

Player.IShopUser.get_Money 与 Player.get_Inventory 共享 RVA 0x7FD2F0，机器码返回同一个 inventory 引用。这把商店使用的 IMoneyContainer 与玩家库存对象直接联系起来。

**尚未确定：** 当前 PlayerManager 单例地址、当前 PlayerInventory 实例地址、当前余额和对象生命周期。以上偏移仅作本版本证据，不是建议采用固定偏移写值。

## 3. 核心金额逻辑：机器码已核对的部分

| 方法 | RVA（本版本核对用） | 静态确认的逻辑 |
|---|---|---|
| PlayerInventory.get_Money | 0x95C750 | 从 this+0x38 读取 32 位整数 |
| PlayerInventory.set_Money | 0x2F0B9A0 | 保存旧值，将输入按有符号整数与零比较，写入 max(value, 0)，随后处理通知锁与变更事件 |
| ChangeMoneyWithoutReceipt(int) | 0x2F0BFE0 | 旧余额加 amount，然后尾调用 set_Money |
| ReceiveMoney(int)，私有 DevOption | 0x2F0BFE0 | 与上述入口共享地址 |
| TakeMoney(int)，私有 DevOption | 0x2F0C460 | 旧余额减 amount，然后尾调用 set_Money |
| ChangeMoneyWithReceipt<T> | 0x1A32180 | 读取 receipt.Amount（+0x14），加上余额，再调用 set_Money；随后进入收据相关路径 |
| OnMoneyEventLockChanged | 0x2F0BE70 | 方法存在已确认，完整方法体尚未核对 |

手工核对的短指令序列：

```text
get_Money:
  8B 41 38          mov eax, [rcx+38h]
  C3                ret

ChangeMoneyWithoutReceipt:
  03 51 38          add edx, [rcx+38h]
  45 33 C0          xor r8d, r8d
  E9 B5 F9 FF FF    jmp RVA 2F0B9A0

set_Money 的金额写入片段（输入暂存在 edi）：
  8B 73 38          mov esi, [rbx+38h]    ; old
  33 C0             xor eax, eax
  85 FF             test edi, edi
  0F 49 C7          cmovns eax, edi
  48 8B 7B 30       mov rdi, [rbx+30h]
  89 43 38          mov [rbx+38h], eax
```

setter 在通知被锁住时，仅在尚未保存旧值的情况下保存此前余额；未锁住的路径将旧值、新值传给 ActionNonAlloc<int,int>.Call（目标 RVA 0x11745B0，script.json 确认符号）。这意味着直接改 _money 会绕过已存在的通知逻辑。解锁后的完整刷新行为尚未逐指令核对。

ChangeMoneyWithReceipt 的函数内 +0x57 处 call 解析为 set_Money。对应前置指令读取 receipt+0x14，再与 this+0x38 相加。其后的泛型分发与完整收据合并流程未全面解码，不能把元数据接口当成已验证的完整实现。

以上算术指令为 32 位加减；没有在这些短入口看到 checked 溢出检查。不要据此声称任意大数输入安全。

## 4. 核心程序集包含的功能与逻辑

下表区分接口/字段证据与完整算法：除上一节的小段机器码外，复杂功能主要由元数据结构和签名确认存在，未执行游戏内行为。

| 子系统 | 已确认类型/入口 | 可据此确认的功能范围；未确认的内容 |
|---|---|---|
| 余额与库存 | IMoneyContainer、IInventory、PlayerInventory | 读写余额、带/不带收据变更、金额事件锁、物品容器；当前实例未定位 |
| 玩家买卖 | PlayerManager.Player.TryMakePurchase、三个 TryMakeSale 重载、PayRent | 接口接收物品容器、价格、物品实例/数量；存在买卖及付租业务入口，交易校验及回滚顺序未核对 |
| 收据与账目 | ReceiptBase、ReceiptList、BaseReceiptList、ReceiptsLookup | 时间、Amount、Count、合并、按类型/天查询、存取；ReceiptType 包含 Rent、Restaurant、Shop、Staff、BoatFuel |
| 日/总收支 | Player.GetDayBalance、GetTotalBalance | loss/gain 汇总接口；收入支出分类与符号细节未解码 |
| 市场模拟 | EconomyManager | 商家库存、补货、市场/买卖价格、新鲜度参数、议价技能、关系、每日特价、短缺/充裕、交易经验；确切价格公式与触发条件未解码 |
| 商店交易请求 | ShopTradeRequest | customer、shop、目的容器、itemType、freshness、amount 和 CalculateTotalPrice |
| 经营与工资 | VenueFinances | 按日利润、CalculateProfitsForDay、CalculateStaffPayment、Save/Load；净利润公式未解码 |
| 租赁 | RentManager | 开始/停止租赁、租户解析、CollectReceipts/CollectReceipt、写入存档包；结算时点和金额计算待核对 |
| AI 资产所有者 | Nivalis.Locale.AIOwner | 自己的 money 字段、IMoneyContainer、IPropertyOwner、ISaveable；不是玩家余额的同一字段 |
| 剧情同步 | ArticyGlobalInventoryLinker | OnPlayerMoneyChanged、OnGlobalInventoryLimsChanged、OnPlayerMoneyChangedByStory、重入保护字段；表明有双向同步接口，完整传播链待验证 |
| 存档 | PlayerSave、PlayerManager、SerializationManager | 玩家快照含 Money、房产、个人收据；存在快照、Save/Load、WriteToPacket 和全局存取入口，未修改存档或验证往返恢复 |
| 显示 | PlayerMoneyDisplayUI | UpdateMoney(previousValue,newValue)，与权威存储类分离 |

Assembly-CSharp 的命名空间还覆盖对话、AI/顾客循环、技能、制作、钓鱼、本地化、UI 和开发选项。这里只确认这些类型分组存在，没有声称已恢复整个游戏源码或其完整算法。

## 5. 后续运行验证入口

1. 工具审批策略允许 attach_process 后，重新确认 PID 与加载模块身份，再建立并最终清理自己的调试会话。用户已授权读写，无需重新询问同一授权；目前阻碍来自工具策略。
2. 动态解析 PlayerManager / Player / PlayerInventory，验证实例和线程，记录当前余额。
3. 观察一次正常交易进入哪个重载、set_Money 的调用栈、旧/新金额及收据事件，再与 UI/剧情变量对照。
4. 对买卖、租金、经营工资和 Save/Load 分别核对，不能用一次 setter 命中代表全部功能验证。
5. 如要实现调整余额，优先原有 ChangeMoneyWithoutReceipt 或符合业务含义的带收据入口；不会只凭字段存在就调用未知实例。

## 6. 证据与复现

- 原始元数据：work/il2cpp-validation/dump.cs；程序集 image 在第 98 行，PlayerInventory 第 755492 行，PlayerSave 第 714116 行，PlayerManager.Player 第 714267 行，PlayerManager 第 714590 行，EconomyManager 第 784043 行附近。
- 脚本方法映射：work/il2cpp-validation/script.json。
- 本轮分析脚本：work/money-investigation.py（只读游戏文件；输出仅在工作区）。
- 类原文摘录：work/money-investigation/selected-types.txt（包含原始行号与 RVA）。
- 简化类型列表：work/money-investigation/selected-types-brief.txt。
- 金额机器码：work/money-investigation/money-machine-bytes.json。ChangeMoneyWithReceipt 的取样窗口包含后续相邻代码，不能把整个窗口都解释为同一个方法。
- 分支候选：work/money-investigation/branch-candidates.json。该文件是 E8/E9 字节扫描候选，不能单独证明指令边界；本文只对明确列出的短指令和调用点作人工核对。共享泛型地址不能只凭首个符号名判断具体类型。
- 关键完整方法签名：work/money-investigation/selected-methods.json。

结论边界：已找到余额存储、金额修改的共同入口以及所属业务程序集；运行时附加与实值验证受工具策略阻碍，未完成。
