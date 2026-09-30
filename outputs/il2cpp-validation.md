# Nivalis Nights IL2CPP 技术验证

日期：2026-09-30。仅离线导出与静态验证；本轮没有附加进程、注入、修改游戏文件或内存。

## 结论

真实游戏的元数据导出、MCP 加载、符号搜索、类字段查询和两个方法的反编译均成功。现有工具组合足以继续逆向调查，但复杂伪代码存在已观察到的类型恢复错误，不能直接作为修改器实现依据。没有验证任何游戏修改效果。

## 输入身份

- 二进制：D:/Steam/steamapps/common/Nivalis Nights/GameAssembly.dll
- SHA256：9A0E32C2D09A5025F867D29BF39B9BEDD0715B513456617FBFD82C581E1A376D
- 元数据：D:/Steam/steamapps/common/Nivalis Nights/Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat
- SHA256：C8BD44F74B47136AEAD259DC2B88F289C12CB01E083FEEECDCD096A6FC1B2CF9

## 实测结果

1. Il2CppDumper 读取版本 27，自动切换内部 IL2CPP 解析版本为 27.1，定位 CodeRegistration 0x183491400、MetadataRegistration 0x183BC1590。导出完成，退出码 0。这不是 Unity 引擎版本变更。
2. 输出目录：D:/NightsHack/work/il2cpp-validation。已生成 script.json、dump.cs、il2cpp.h、stringliteral.json 和 DummyDll。DummyDll 是元数据辅助产物，不是恢复后的完整游戏实现。
3. 通过 stdio JSON-RPC 调用 il2cpp-decompiler-mcp 1.1.0；当前会话原生工具目录仍未暴露该服务器。
4. list_dumps 未发现 AppData 缓存，但绝对路径 load_project 成功：243,784 条方法记录、29,761 个类索引项、Field layouts loaded=true。方法记录包含泛型实例和共享地址，不能视为同数量独立函数。
5. search_symbols 和 get_class_info 成功查询 Nivalis.PlayerMoneyDisplayUI。moneyTMP、deltaHolder、moneyDeltaText、_player 的偏移分别为 0x18、0x20、0x28、0x30，与 dump.cs 的直接字段声明一致。继承字段未逐项验证。
6. decompile_method 成功返回 PlayerMoneyDisplayUI.UpdateMoney 和 InventorySystem.FoodItemType.get_ConsumeTime 的伪代码。七条 JSON-RPC 响应无 RPC/tool error，MCP 退出码 0。

## 原始机器码核对

FoodItemType.get_ConsumeTime：RVA 0x2ED31B0，PE 节 il2cpp，文件偏移 0x2ED23B0。读取机器码为 8B 81 60 01 00 00 C3，解码为 mov eax,[rcx+0x160]; ret。dump.cs 声明 consumeTime 为偏移 0x160 的 int；MCP 还原 return this.consumeTime。三者一致。

UpdateMoney：RVA 0x2F0E9A0，文件偏移 0x2F0DBA0。已读取入口机器码，未对整个函数作逐指令验证。script.json 与 dump.cs 的参数类型是 int previousValue、int newValue，MCP 却输出 object previousValue、object newValue。还存在 ebp/rcx/v9 等未恢复或未声明变量、field_0xNN、test_cond 和原始指令注释。输出适合作为分析线索，不能视为可编译 C# 或完整正确的源码。

## 边界与后续

- 已证明：该安装版本可以导出元数据和地址映射，能通过此 MCP 查询并反编译真实方法；简单 getter 样本与机器码一致。
- 未证明：复杂方法全面正确、运行时对象定位可靠、任何金钱/背包修改生效、版本更新后偏移稳定。UpdateMoney 是显示层样本，不能据此认定为权威金钱写入点。
- 后续采用 IL2CPP 映射定位候选，再以 Ghidra/WinDbg 核对。Ghidra 本轮未执行导入或反编译；WinDbg 基础通路在上一轮测试通过。
- 本轮结束查询 PID 40208 仍存在且 Responding=True；未做画面或游戏逻辑验证。

## 原始证据

- D:/NightsHack/work/il2cpp-validation/mcp-results.jsonl
- D:/NightsHack/work/il2cpp-validation/mcp.stderr.log
- D:/NightsHack/work/il2cpp-validation/script.json
- D:/NightsHack/work/il2cpp-validation/dump.cs

本轮未更改工具代码和配置，新增内容均位于 D:/NightsHack。
