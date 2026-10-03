using System.Diagnostics;
using NightsHack.Logging;
using System.Text.Json;
using System.IO.Pipes;
using System.Text;

namespace NightsHack.Modifier;

internal sealed class MainForm : Form
{
    readonly Button manualButton = new() { Text = "手动注入", Width = 180, Height = 42 };
    readonly TextBox status = LogBox();
    readonly TabControl pages = new() { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed };
    readonly TabPage homePage = new("首页");
    readonly TabPage basicPage = new("基础选项") { Enabled = false };
    readonly NumericUpDown moneyAmount = MoneyInput();
    readonly Button setMoneyButton = new() { Text = "确定", Width = 120, Height = 34 };
    readonly Button probePlayerButton = new() { Text = "探测玩家数据", Width = 180, Height = 42 };
    readonly System.Windows.Forms.Timer hookStateTimer = new() { Interval = 1000 };
    readonly EventJournal journal;
    bool busy;
    Guid request;
    int targetPid;
    DateTime targetStartTimeUtc;
    bool? testHookState;
    static TextBox LogBox() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9) };
    const int GameMoneyPerModifierUnit = 100;
    const int SafeMaximumGameMoney = int.MaxValue - 1;
    const decimal SafeMaximumModifierMoney = SafeMaximumGameMoney / (decimal)GameMoneyPerModifierUnit;
    static NumericUpDown MoneyInput() => new() { Minimum = 0.01m, Maximum = SafeMaximumModifierMoney, DecimalPlaces = 2, Increment = 0.01m, ThousandsSeparator = true, Width = 150, Height = 32, Value = 1.00m };
    internal static bool TryParseMoneyAmount(string text, out int gameMoney)
    {
        gameMoney = 0;
        return decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var modifierMoney)
            && TryConvertModifierMoney(modifierMoney, out gameMoney);
    }
    internal static bool TryConvertModifierMoney(decimal modifierMoney, out int gameMoney)
    {
        gameMoney = 0;
        if (modifierMoney <= 0 || decimal.Round(modifierMoney, 2) != modifierMoney) return false;
        decimal scaled = modifierMoney * GameMoneyPerModifierUnit;
        if (scaled > SafeMaximumGameMoney) return false;
        gameMoney = decimal.ToInt32(scaled);
        return gameMoney > 0;
    }
    public MainForm()
    {
        status.Name = "injectorLog";
        string version = "v1.1.1-DevFix";
        string build = Path.Combine(AppContext.BaseDirectory, "build.json");
        if (File.Exists(build))
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(build));
            version = json.RootElement.GetProperty("Version").GetString() ?? version;
        }
        Text = "Kainy's Nivalis Nights Trainer · " + version;
        ClientSize = new Size(760, 380); MinimumSize = new Size(580, 300);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        journal = new EventJournal(Path.Combine(AppContext.BaseDirectory, "logs", "injector-" + LogSession.Current + ".jsonl"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var donationButton = new Button { Text = "无偿捐赠作者", Width = 180, Height = 42, Margin = new Padding(12, 3, 3, 3) };
        donationButton.Click += (_, _) => { using var dialog = new DonationForm(); dialog.ShowDialog(this); };
        actions.Controls.Add(manualButton);
        actions.Controls.Add(probePlayerButton);
        actions.Controls.Add(donationButton);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(new Label { Text = "注入器日志", AutoSize = true }, 0, 1);
        layout.Controls.Add(status, 0, 2);
        homePage.Controls.Add(layout);
        BuildBasicPage();
        pages.TabPages.AddRange(new[] { homePage, basicPage });
        pages.Selecting += (_, e) => { if (e.TabPage == basicPage && !basicPage.Enabled) e.Cancel = true; };
        pages.DrawItem += (_, e) =>
        {
            var page = pages.TabPages[e.Index];
            TextRenderer.DrawText(e.Graphics, page.Text, Font, e.Bounds,
                page.Enabled ? SystemColors.ControlText : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        Controls.Add(pages);
        manualButton.Click += async (_, _) => await RunAction();
        setMoneyButton.Click += (_, _) => ExecuteMoneyCommand();
        probePlayerButton.Click += async (_, _) => await ProbePlayerAsync();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; Log("OPERATION_IN_PROGRESS", "注入操作尚未结束，请等待结果。", "WARN"); } };
        hookStateTimer.Tick += (_, _) => RefreshHookState();
        hookStateTimer.Start();
        FormClosed += (_, _) => { hookStateTimer.Stop(); hookStateTimer.Dispose(); journal.Dispose(); };
        Log("TRAINER_READY", "初始化完成。日志文件：" + journal.Path);
    }

    void BuildBasicPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(24), ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var amountRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        amountRow.Controls.Add(new Label { Text = "修改余额为", AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
        amountRow.Controls.Add(moneyAmount);
        amountRow.Controls.Add(setMoneyButton);
        layout.Controls.Add(amountRow, 0, 0);
        layout.Controls.Add(new Label { Text = "范围 0.01 - 21474836.46", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 1);
        basicPage.Controls.Add(layout);
    }

    void ExecuteMoneyCommand()
    {
        if (TryConvertModifierMoney(moneyAmount.Value, out int amount))
            _ = ExecuteSetMoneyAsync(amount);
        else
            Log("MONEY_REJECTED", "金币数值无效或超出范围。请输入 0.01 到 21474836.46，最多两位小数。", "WARN", "Rejected");
    }

    async Task ExecuteSetMoneyAsync(int value)
    {
        setMoneyButton.Enabled = false;
        Guid id = Guid.NewGuid();
        try
        {
            var command = new PipeCommandRequest(id, "Player", "Nivalis.PlayerManager.LocalPlayer.Inventory.Money", "Set", JsonSerializer.Serialize(new { value }));
            var ack = await SendPipeCommandAsync("NightsHack.Command." + targetPid, command, TimeSpan.FromSeconds(3));
            Log("MONEY_COMMAND_ACK", ack.Summary, ack.Succeeded ? "INFO" : "WARN", ack.State);
            if (!ack.Succeeded) return;
            bool completed = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                var poll = new PipeCommandRequest(Guid.NewGuid(), "Player", "Nivalis.PlayerManager.LocalPlayer.Inventory.Money", "GetResult", JsonSerializer.Serialize(new { requestId = id }));
                var reply = await SendPipeCommandAsync("NightsHack.Command." + targetPid, poll, TimeSpan.FromSeconds(1));
                if (reply.State is "Completed" or "SideEffectsUnknown" or "Rejected")
                {
                    completed = true;
                    Log("MONEY_COMMAND_RESULT", reply.Summary + (reply.ResultJson == "{}" ? "" : "; " + reply.ResultJson), reply.Succeeded ? "INFO" : "WARN", reply.State);
                    break;
                }
            }
            if (!completed) Log("MONEY_COMMAND_TIMEOUT", "游戏线程在 8 秒内没有返回金币结果；请查看 BepInEx 日志中的 COMMAND_TICK/COMMAND_FAILED。", "WARN", "Pending");
        }
        catch (Exception error) { Log("MONEY_COMMAND_FAILED", error.GetBaseException().Message, "ERROR", "Failed"); }
        finally { setMoneyButton.Enabled = basicPage.Enabled; }
    }

    async Task ProbePlayerAsync()
    {
        if (targetPid <= 0 || !HookProcessIsAlive())
        {
            return;
        }
        probePlayerButton.Enabled = false;
        Guid id = Guid.NewGuid();
        try
        {
            var command = new PipeCommandRequest(id, "Player", "Nivalis.PlayerManager.LocalPlayer.Inventory", "Probe", "{}");
            var ack = await SendPipeCommandAsync("NightsHack.Command." + targetPid, command, TimeSpan.FromSeconds(3));
            Log("PLAYER_PROBE_ACK", ack.Summary, ack.Succeeded ? "INFO" : "WARN", ack.State);
            if (!ack.Succeeded)
            {
                return;
            }
            bool completed = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                var poll = new PipeCommandRequest(Guid.NewGuid(), "Player", "Nivalis.PlayerManager.LocalPlayer.Inventory", "GetResult", JsonSerializer.Serialize(new { requestId = id }));
                var reply = await SendPipeCommandAsync("NightsHack.Command." + targetPid, poll, TimeSpan.FromSeconds(1));
                if (reply.State is "Completed" or "SideEffectsUnknown" or "Rejected")
                {
                    completed = true;
                    Log("PLAYER_PROBE_RESULT", reply.Summary + (reply.ResultJson == "{}" ? "" : "; " + reply.ResultJson), reply.Succeeded ? "INFO" : "WARN", reply.State);
                    break;
                }
            }
            if (!completed) Log("PLAYER_PROBE_TIMEOUT", "游戏线程在 8 秒内没有返回玩家数据；请查看 BepInEx 日志中的 COMMAND_TICK/COMMAND_FAILED。", "WARN", "Pending");
        }
        catch (Exception error) { Log("PLAYER_PROBE_FAILED", error.GetBaseException().Message, "ERROR", "Failed"); }
        finally { probePlayerButton.Enabled = basicPage.Enabled; }
    }

    static async Task<PipeCommandResponse> SendPipeCommandAsync(string pipeName, PipeCommandRequest request, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        string? line = await reader.ReadLineAsync().WaitAsync(timeoutSource.Token);
        return line is null ? new(request.RequestId, "Unavailable", false, "Pipe closed before response.", "{}")
            : JsonSerializer.Deserialize<PipeCommandResponse>(line) ?? new(request.RequestId, "Unavailable", false, "Invalid response.", "{}");
    }

    sealed record PipeCommandRequest(Guid RequestId, string Feature, string TargetId, string Operation, string ArgumentsJson);
    sealed record PipeCommandResponse(Guid RequestId, string State, bool Succeeded, string Summary, string ResultJson);

    internal void SetHooksReady(bool ready)
    {
        if (ready && !HookProcessIsAlive()) ready = false;
        basicPage.Enabled = ready;
        if (!ready) pages.SelectedTab = homePage;
        pages.Invalidate();
    }

    void RefreshHookState()
    {
        if (busy || !basicPage.Enabled) return;
        if (!HookProcessIsAlive())
        {
            SetHooksReady(false);
            Log("HOOK_LOCKED", "未检测到仍在运行且包含 CoreCLR 的目标游戏进程，基础选项已锁定。", "WARN", "Locked");
        }
    }

    internal void RefreshHookStateForTest() => RefreshHookState();

    bool HookProcessIsAlive()
    {
        if (testHookState.HasValue) return testHookState.Value;
        if (targetPid <= 0 || targetStartTimeUtc == default) return false;
        try
        {
            using var process = Process.GetProcessById(targetPid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != targetStartTimeUtc) return false;
            return process.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    internal void SetTestHookState(bool? state) => testHookState = state;

    async Task RunAction()
    {
        if (busy) return;
        if (basicPage.Enabled && HookProcessIsAlive())
        {
            Log("INJECTION_ALREADY_READY", "当前游戏会话已注入并就绪，无需重复注入。", outcome: "AlreadyReady");
            return;
        }
        busy = true; request = Guid.NewGuid(); targetPid = 0;
        SetHooksReady(false);
        manualButton.Enabled = false;
        IProgress<InjectorEvent> progress = new Progress<InjectorEvent>(item => {
            if (item.TargetProcessId != 0) targetPid = item.TargetProcessId;
            Log(item.Code, item.Message, item.Level);
        });
        try
        {
            Log("INJECTION_REQUESTED", "已接收手动注入请求；正在查找运行中的游戏进程。");
            await Task.Run(() =>
            {
                using var game = GameInstall.FindRunningGame();
                string executable = game.MainModule?.FileName ?? throw new InvalidOperationException("无法读取目标游戏进程路径，请使用与游戏相同的权限运行修改器。");
                progress.Report(new("PROCESS_SELECTED", $"已定位游戏进程 PID={game.Id}；开始兼容性与完整性校验。", TargetProcessId: game.Id));
                new GameInstall(executable, AppContext.BaseDirectory).InjectDynamic(progress, request, game);
            });
            using (var current = GameInstall.FindRunningGame())
                targetStartTimeUtc = current.StartTime.ToUniversalTime();
            Log("INJECTION_COMPLETED", "手动注入与 PlayerHook、WorldHook、ItemHook、GameRuntimeHook 就绪日志验证完成。", outcome: "Succeeded");
            SetHooksReady(true);
        }
        catch (Exception error) { Log("INJECTION_FAILED", error.GetBaseException().Message, "ERROR", "Failed"); }
        finally { busy = false; manualButton.Enabled = true; }
    }
    void Log(string code, string text, string level = "INFO", string outcome = "Progress")
    {
        try
        {
            var record = journal.Write(new AuditRecord { Component = "Injector", Code = code, Level = level, RequestId = request == Guid.Empty ? "" : request.ToString("D"), Target = targetPid == 0 ? "" : "PID=" + targetPid, Operation = "DynamicInject", Outcome = outcome, Message = text });
            Append(status, record.ToDisplay());
        }
        catch (Exception error) { Append(status, "[ERROR] [INJECTOR_LOG_WRITE_FAILED] " + error.Message); }
    }
    static void Append(TextBox box, string line)
    {
        if (box.TextLength > 200000) { box.Select(0, 100000); box.SelectedText = ""; }
        box.AppendText(line + Environment.NewLine);
    }
}
