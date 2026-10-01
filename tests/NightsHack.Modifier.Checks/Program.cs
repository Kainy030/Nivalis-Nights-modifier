using System.Reflection;
using System.Text.Json;
using NightsHack.Modifier;

internal static class Checks
{
    sealed class Progress : IProgress<InjectorEvent> { public void Report(InjectorEvent message) => Console.WriteLine($"[{message.Level}] [{message.Code}] {message.Message}"); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--dynamic")
            {
                var install = new GameInstall(args[1], args[2]);
                install.InjectDynamic(new Progress());
                return 0;
            }
            string scratch = Path.GetFullPath(Path.Combine("work", "modifier-checks", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(scratch);
            string game = Path.Combine(scratch, "game"), package = Path.Combine(scratch, "package");
            Directory.CreateDirectory(game); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(game, GameInstall.ExeName), "test placeholder, not executable");
            var instance = new GameInstall(Path.Combine(game, GameInstall.ExeName), package) { TestProcessProbe = () => new() };
            int count = 0;
            void Check(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
            void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
            void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
            void Deploy(Dictionary<string,string> plan) => typeof(GameInstall).GetMethod("Deploy", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, [plan, new Progress()]);
            string Add(string relative, string text)
            {
                string path = GameInstall.Under(package, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text); return GameInstall.Hash(path);
            }
            Check("process discovery rejects missing or ambiguous targets", () => {
                Reject(() => GameInstall.SelectRunningGame([]));
                Reject(() => GameInstall.SelectRunningGame([System.Diagnostics.Process.GetCurrentProcess(), System.Diagnostics.Process.GetCurrentProcess()]));
                using var target = System.Diagnostics.Process.GetCurrentProcess();
                Assert(ReferenceEquals(GameInstall.SelectRunningGame([target]), target));
            });
            Check("target path rejects traversal and absolute paths", () => {
                Reject(() => GameInstall.Under(game, "../outside.dll"));
                Reject(() => GameInstall.Under(game, Path.GetFullPath("outside.dll")));
            });
            Check("dynamic deployment preserves unrelated files", () => {
                File.WriteAllText(Path.Combine(game, "keep.cfg"), "user config");
                Deploy(new() { ["payload/hooks/PlayerHook.dll"] = Add("payload/hooks/PlayerHook.dll", "test-plugin") });
                Assert(File.ReadAllText(Path.Combine(game, "BepInEx/plugins/NightsHack/PlayerHook.dll")) == "test-plugin");
                Assert(File.ReadAllText(Path.Combine(game, "keep.cfg")) == "user config");
            });
            Check("static startup payload is rejected before writes", () => {
                foreach (string name in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "WINHTTP.DLL" })
                {
                    Reject(() => Deploy(new() { ["payload/runtime/" + name] = Add("payload/runtime/" + name, "test") }));
                    Assert(!File.Exists(Path.Combine(game, name)));
                }
                Assert(typeof(GameInstall).GetMethod("InstallStatic") == null);
            });            Check("failed deployment restores prior plugin and removes new files", () => {
                // The second copy fails after the first copy succeeds: rollback must remove first and restore second.
                string invalid = Add("payload/hooks/PlayerHook.dll", "replacement");
                Reject(() => Deploy(new() { ["payload/hooks/WorldHook.dll"] = Add("payload/hooks/WorldHook.dll", "new"),
                    ["payload/hooks/PlayerHook.dll"] = "deliberately-invalid-hash" }));
                Assert(!File.Exists(Path.Combine(game, "BepInEx/plugins/NightsHack/WorldHook.dll")));
                Assert(File.ReadAllText(Path.Combine(game, "BepInEx/plugins/NightsHack/PlayerHook.dll")) == "test-plugin");
            });
            Check("duplicate plugin folders are refused", () => {
                string duplicate = Path.Combine(game, "BepInEx/plugins/PlayerHook.dll"); File.WriteAllText(duplicate, "duplicate");
                Reject(() => Deploy(new())); File.Delete(duplicate);
            });
            Check("tampered bundle is rejected", () => {
                File.WriteAllText(Path.Combine(package, "payload.sha256.json"), JsonSerializer.Serialize(new Dictionary<string,string> { ["payload/hooks/PlayerHook.dll"] = "bad" }));
                Reject(() => typeof(GameInstall).GetMethod("VerifyPackage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, null));
            });
            Check("audit records are durable and tail handles partial UTF8", () => {
                string path = Path.Combine(scratch, "tail.jsonl");
                var tail = new HookLogTail(path);
                var record = new NightsHack.Logging.AuditRecord { Message = "中文记录", Code = "TEST", Target = "fixture" };
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
                int split = Array.IndexOf(bytes, (byte)0xe4) + 1;
                File.WriteAllBytes(path, bytes[..split]); Assert(!tail.Read().Any());
                using (var append = new FileStream(path, FileMode.Append)) append.Write(bytes[split..]);
                Assert(tail.Read().Single().Message == "中文记录"); Assert(!tail.Read().Any());
                using var journal = new NightsHack.Logging.EventJournal(Path.Combine(scratch, "journal.jsonl"));
                journal.Write(record); Assert(new HookLogTail(journal.Path).Read().Single().Sequence == 1);
            });
            Check("missing audit prevents command execution", () => {
                bool invoked = false;
                Reject(() => NightsHack.HookRuntime.HookAudit.Execute<int>(Guid.NewGuid(), "Fixture", "FixtureHook", "Fixture.Target", "Test", () => () => { invoked = true; return new(true, 0, "unexpected"); }));
                Assert(!invoked);
            });
            Check("command audit records execution order and rejected validation", () => {
                NightsHack.HookRuntime.HookAudit.Initialize(Path.Combine(scratch, "hooks"));
                var id = Guid.NewGuid(); bool called = false;
                var result = NightsHack.HookRuntime.HookAudit.Execute<int>(id, "Fixture", "FixtureHook", "Fixture.Target", "Test", () => () => {
                    called = true; return new NightsHack.HookRuntime.HookCommandResult<int>(true, 7, "Fixture result=7");
                });
                Assert(called && result.Value == 7);
                var records = new HookLogTail(NightsHack.HookRuntime.HookAudit.LogPath!).Read().Where(r => r.RequestId == id.ToString("D")).ToArray();
                Assert(records.Select(r => r.Code).SequenceEqual(new[] { "COMMAND_RECEIVED", "HOOK_TARGET_READY", "HOOK_CALL_BEGIN", "HOOK_CALL_END", "COMMAND_COMPLETED" }));
                var rejected = Guid.NewGuid();
                Reject(() => NightsHack.HookRuntime.HookAudit.Execute<int>(rejected, "Fixture", "FixtureHook", "Fixture.Target", "Test", () => throw new InvalidOperationException("invalid target")));
                var failed = new HookLogTail(NightsHack.HookRuntime.HookAudit.LogPath!).Read().Where(r => r.RequestId == rejected.ToString("D")).ToArray();
                Assert(failed.Length == 2 && failed[^1].Outcome == "NotExecuted");
            });
            Check("command execution failure preserves uncertain side effects", () => {
                var id = Guid.NewGuid();
                Reject(() => NightsHack.HookRuntime.HookAudit.Execute<int>(id, "Fixture", "FixtureHook", "Fixture.Target", "Test", () => () => throw new InvalidOperationException("fixture invocation failed")));
                var records = new HookLogTail(NightsHack.HookRuntime.HookAudit.LogPath!).Read().Where(r => r.RequestId == id.ToString("D")).ToArray();
                Assert(records[^1].Code == "COMMAND_FAILED" && records[^1].Outcome == "SideEffectsUnknown");
                Assert(!records.Any(r => r.Code == "COMMAND_COMPLETED"));
            });
            Check("money input accepts positive modifier decimals and converts to game cents", () => {
                Assert(MainForm.TryParseMoneyAmount("1", out var one) && one == 100);
                Assert(MainForm.TryParseMoneyAmount("1.23", out var cents) && cents == 123);
                Assert(MainForm.TryParseMoneyAmount("21474836.46", out var max) && max == int.MaxValue - 1);
                foreach (string value in new[] { "", "0", "-1", "0.001", "abc", "21474836.47", "21474836.48", " 1 " })
                    Assert(!MainForm.TryParseMoneyAmount(value, out _));
            });
            Check("main form has money controls and gated features", () => {
                Application.SetHighDpiMode(HighDpiMode.SystemAware); Application.EnableVisualStyles();
                using var form = new MainForm(); form.Show(); Application.DoEvents();
                IEnumerable<Control> Flatten(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] {x}.Concat(Flatten(x)));
                var labels = Flatten(form).OfType<Button>().Select(b => b.Text).ToArray();
                Assert(labels.Contains("手动注入") && labels.Contains("无偿捐赠作者") && labels.Contains("探测玩家数据") && labels.Contains("确定") && !labels.Contains("增加金钱") && !labels.Contains("减少金钱") && form.Text.StartsWith("Kainy's Nivalis Nights Trainer · "));
                var topButtons = Flatten(form).OfType<Button>().Where(b => b.Text is "手动注入" or "探测玩家数据" or "无偿捐赠作者").ToArray();
                Assert(topButtons.Length == 3 && topButtons.All(b => b.Width == 180 && b.Height == 42));
                var textLabels = Flatten(form).OfType<Label>().Select(l => l.Text).ToArray();
                Assert(textLabels.Contains("修改余额为") && textLabels.Contains("范围 0.01 - 21474836.46"));
                var pages = Flatten(form).OfType<TabControl>().Single();
                Assert(pages.TabPages.Cast<TabPage>().Select(p => p.Text).SequenceEqual(new[] { "首页", "基础选项" }));
                var amounts = Flatten(form).OfType<NumericUpDown>().ToArray();
                Assert(amounts.Length == 1 && amounts.All(a => a.Minimum == 0.01m && a.Maximum == 21474836.46m && a.DecimalPlaces == 2 && a.Increment == 0.01m));
                Assert(pages.SelectedIndex == 0 && !pages.TabPages[1].Enabled);
                pages.SelectedIndex = 1;
                Assert(pages.SelectedIndex == 0);
                form.SetTestHookState(true);
                form.SetHooksReady(true);
                pages.SelectedIndex = 1;
                Assert(pages.SelectedIndex == 1 && pages.TabPages[1].Enabled);
                pages.SelectedIndex = 0;
                var manual = topButtons.Single(b => b.Text == "手动注入");
                manual.PerformClick();
                manual.PerformClick();
                Assert(pages.TabPages[1].Enabled && manual.Enabled);
                Assert(Flatten(form).OfType<TextBox>().Single(t => t.Multiline).Text.Contains("INJECTION_ALREADY_READY"));
                pages.SelectedIndex = 1;
                Assert(pages.SelectedIndex == 1);
                form.SetTestHookState(false);
                form.RefreshHookStateForTest();
                Assert(pages.SelectedIndex == 0 && !pages.TabPages[1].Enabled);
                form.SetTestHookState(true);
                form.SetHooksReady(true);
                form.SetHooksReady(false);
                Assert(pages.SelectedIndex == 0 && !pages.TabPages[1].Enabled);
                pages.SelectedIndex = 1;
                Assert(pages.SelectedIndex == 0);
                var logBoxes = Flatten(form).OfType<TextBox>().Where(t => t.Multiline).ToArray();
                Assert(logBoxes.Length == 1 && logBoxes[0].ReadOnly);
                Assert(!Flatten(form).Any(c => c.Text.Contains("游戏程序") || c.Text.Contains("空闲时") || c.Text.Contains("默认关闭观察") || c.Text == "实时钩子日志"));
                using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, form.ClientRectangle);
                bitmap.Save(Path.Combine(scratch, "modifier-ui.png"));
                Console.WriteLine("UI image: " + Path.Combine(scratch, "modifier-ui.png"));
                form.Close();
            });
            Console.WriteLine($"{count} modifier checks passed. These do not prove game integration.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.GetBaseException()); return 1; }
    }
}
