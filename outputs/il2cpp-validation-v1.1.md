# v1.1 游戏升级反编译记录

日期：2026-10-03
进程：PID 17912（已通过 WinDbg attach 确认）
游戏目录：D:/Steam/steamapps/common/Nivalis Nights

## 新输入身份

- GameAssembly.dll SHA256：`0DA6AAC5209F504DA743ABD7926F6F528010E2CA7B884B4A20F5198F42F1A26D`
- global-metadata.dat SHA256：`5139D6BB87229495DE92FEC78F5F253E31C7D05BFE69A950CAE73C90975747E3`
- script.json SHA256：`6790B68BE6D2460748521D09E4260DB786A98E8C8E5661F165F069EC1034C2C9`
- Il2CppDumper：Metadata 27，IL2CPP 27，自动切换 27.1
- CodeRegistration：`0x183BE45E0`
- MetadataRegistration：`0x183BE66F0`
- 导出目录：`work/il2cpp-validation-v1.1`
- IL2CPP MCP 加载：244,279 methods，29,817 classes，field layouts loaded

## 关键映射

`Nivalis.InventorySystem.PlayerInventory`：

- `_money`：字段偏移 `0x38`
- `set_Money`：`0x18067FC40`
- `get_Money`：`0x18067FC30`
- `AddItem`：`0x1806802B0`
- `ChangeMoneyWithoutReceipt`：`0x1806802A0`
- `ReceiveMoney`：`0x1806802A0`
- `TakeMoney`：`0x180680720`

`PlayerMoneyDisplayUI.UpdateMoney`：`0x180682C60`

## 限制

新版本反编译数据已生成并加载，关键类和方法已重新定位。当前 decompiler 对 `set_Money`、`AddItem` 的输出仍包含 object 类型和未恢复变量，不能把伪代码直接当作可编译 C#。本轮只更新了游戏身份哈希，尚未重新生成四个插件的完整候选目录，也未重新构建、部署或进行游戏内余额/物品验证。

