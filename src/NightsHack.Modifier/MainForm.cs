using System.Diagnostics;
using NightsHack.Logging;

namespace NightsHack.Modifier;

internal sealed class MainForm : Form
{
    readonly Button manualButton = new() { Text = "手动注入", Width = 160, Height = 42 };
    readonly TextBox status = LogBox();
    readonly EventJournal journal;
    bool busy;
    Guid request;
    int targetPid;
    static TextBox LogBox() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9) };
    public MainForm()
    {
        string version = "v0.4.3-Dev";
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
        actions.Controls.Add(donationButton);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(new Label { Text = "注入器日志", AutoSize = true }, 0, 1);
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);
        manualButton.Click += async (_, _) => await RunAction();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; Log("OPERATION_IN_PROGRESS", "注入操作尚未结束，请等待结果。", "WARN"); } };
        FormClosed += (_, _) => journal.Dispose();        Log("TRAINER_READY", "初始化完成。日志文件：" + journal.Path);
    }

    async Task RunAction()
    {
        if (busy) return;
        busy = true; request = Guid.NewGuid(); targetPid = 0;
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
            Log("INJECTION_COMPLETED", "手动注入与 PlayerHook、WorldHook、ItemHook、GameRuntimeHook 就绪日志验证完成。", outcome: "Succeeded");
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
