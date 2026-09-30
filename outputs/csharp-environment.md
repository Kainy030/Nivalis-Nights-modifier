# C# 环境配置与验证

日期：2026-09-30。

## 配置结果

原检测仅发现 C:/Program Files (x86)/dotnet 下的 x86 .NET 7.0.20 运行库，未找到 SDK。已在工作区安装 x64 .NET SDK 10.0.401，MSBuild 18.9.11，Microsoft.NETCore.App / Microsoft.WindowsDesktop.App 10.0.12。SDK 路径为 D:/NightsHack/.tools/dotnet。

安装包来自 Microsoft 官方发布元数据所列地址，SHA-512 校验一致。来源记录：D:/NightsHack/work/dotnet-setup/sdk-source.json。未覆盖原 x86 运行库，未安装 Visual Studio。

## 使用方法

从 D:/NightsHack 的 PowerShell 执行：

```powershell
& .\Invoke-Dotnet.ps1 --info
& .\Invoke-Dotnet.ps1 build .\work\csharp-validation\CSharpValidation.csproj -c Release
& .\Invoke-Dotnet.ps1 .\work\csharp-validation\bin\Release\net10.0-windows\CSharpValidation.dll
```

Invoke-Dotnet.ps1 使用本地 SDK，构建时将 APPDATA / LOCALAPPDATA 临时定位到工作区，结束后恢复。SDK 用户状态和 NuGet 包缓存也存放在 work 下。没有永久修改系统 PATH；当前 shell 可通过 `. .\Enter-CSharpEnv.ps1` 设置 SDK 路径，但受限环境构建建议使用上述封装。

验证项目的 NuGet.Config 清空网络包源，验证仅依赖 SDK 自带引用包；未来第三方 hook 包的下载与兼容性尚未测试。

## 实测

- Release 构建成功：0 警告、0 错误。
- 程序实际运行成功，退出码 0。
- 输出：PASS: .NET 10.0.12; X64; WinForms handle + event verified.
- 验证覆盖：C# 编译、x64 执行、WinForms 窗口句柄创建、控件容纳和程序化 Click 事件派发。没有做可见 UI 人工操作或 WPF 运行测试。

## 遇到的问题

- PowerShell HTTPS 下载遇到 SSPI 凭证错误，改用本机 Python 的 HTTPS 下载并校验哈希，未关闭证书校验。
- 默认 NuGet 用户配置路径访问被拒，显式 configfile 单独使用仍失败；改用工作区 APPDATA/LOCALAPPDATA 后构建成功。
- 初始测试对隐藏按钮调用 PerformClick 未满足测试条件；改为派生控件调用 OnClick 后事件测试通过。该测试不代表鼠标点击或可见窗口交互通过。

## 后续语言约定

优先用 C# 编写修改器 UI、控制逻辑与可用的托管 hook 层。具体 hook 框架和注入宿主尚未选定；游戏内组件的目标框架必须根据实际加载器要求确定，不能因为安装了 SDK 10 就默认目标游戏能加载 net10.0。此轮没有向游戏注入任何组件，没有修改游戏文件或内存。
