using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;


namespace NightsHack.Modifier;

internal sealed class GameInstall
{
    public const string ExeName = "Nivalis Nights.exe";
    const string AssemblyHash = "9A0E32C2D09A5025F867D29BF39B9BEDD0715B513456617FBFD82C581E1A376D";
    const string MetadataHash = "C8BD44F74B47136AEAD259DC2B88F289C12CB01E083FEEECDCD096A6FC1B2CF9";
    internal static readonly string[] PluginNames = ["PlayerHook", "WorldHook", "ItemHook", "GameRuntimeHook", "NightsHack.HookRuntime"];
    readonly string executable, root, package;
    internal Func<List<Process>>? TestProcessProbe { get; init; }
    public GameInstall(string executable, string package)
    {
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals(ExeName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标进程的可执行文件不是有效的 Nivalis Nights.exe。");
        this.executable = Path.GetFullPath(executable);
        root = Path.GetDirectoryName(this.executable)!;
        this.package = Path.GetFullPath(package);
    }

    public static Process FindRunningGame() => SelectRunningGame(Process.GetProcessesByName("Nivalis Nights"));

    internal static Process SelectRunningGame(Process[] games)
    {
        if (games.Length == 1) return games[0];
        foreach (var game in games) game.Dispose();
        throw new InvalidOperationException(games.Length == 0
            ? "未找到正在运行的目标游戏，请先启动游戏。"
            : "检测到多个游戏进程，请只保留一个后重试。");
    }
    internal static string Hash(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    void VerifyGame(IProgress<InjectorEvent> progress)
    {
        progress.Report(new("VERIFY_BEGIN", "开始验证游戏二进制、元数据和发布包 SHA256。"));
        if (Hash(Path.Combine(root, "GameAssembly.dll")) != AssemblyHash ||
            Hash(Path.Combine(root, "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat")) != MetadataHash)
            throw new InvalidOperationException("游戏版本与已验证的钩子版本不符，未执行注入。");
    }

    Dictionary<string, string> VerifyPackage()
    {
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(package, "payload.sha256.json")))
            ?? throw new InvalidDataException("发布包清单为空。");
        if (manifest.Count == 0) throw new InvalidDataException("发布包缺少文件。");
        foreach (var (relative, expected) in manifest)
        {
            string path = Under(package, relative);
            if (!relative.StartsWith("payload/", StringComparison.Ordinal) || !Hash(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发布包校验失败：" + relative);
        }
        foreach (var required in PluginNames.Select(n => "payload/hooks/" + n + ".dll").Concat(new[] {

            "payload/runtime/dotnet/coreclr.dll", "payload/runtime/BepInEx/core/BepInEx.Unity.IL2CPP.dll",
            "payload/bootstrap/NightsHack.Native.dll", "payload/bootstrap/NightsHack.Bootstrap.dll" }))
            if (!manifest.ContainsKey(required)) throw new InvalidDataException("发布包缺少必需条目：" + required);
        return manifest;
    }

    internal static string Under(string directory, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("不允许绝对部署路径。");
        string basePath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("部署路径越界。");
        for (string? current = Path.GetDirectoryName(path); current != null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("部署路径包含目录链接：" + current);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("部署目标是文件链接。");
        return path;
    }

    List<Process> Running()
    {
        if (TestProcessProbe != null) return TestProcessProbe();
        var matches = new List<Process>();
        foreach (var p in Process.GetProcessesByName("Nivalis Nights"))
        {
            try { if (string.Equals(p.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) { matches.Add(p); continue; } }
            catch { p.Dispose(); throw new InvalidOperationException("无法读取游戏进程，请使用与游戏相同的权限运行修改器。"); }
            p.Dispose();
        }
        return matches;
    }

    public void InjectDynamic(IProgress<InjectorEvent> progress, Guid? requestId = null, Process? expectedProcess = null)
    {
        var running = Running();
        try
        {
            if (running.Count != 1) throw new InvalidOperationException(running.Count == 0 ? "未找到正在运行的目标游戏，请先启动游戏。" : "检测到多个游戏进程，请只保留一个。");
            var game = running[0];
            if (expectedProcess != null && (expectedProcess.HasExited || game.Id != expectedProcess.Id || game.StartTime != expectedProcess.StartTime))
                throw new InvalidOperationException("目标游戏进程已变化，本次注入已取消。请重新点击手动注入。");
            VerifyGame(progress);
            var manifest = VerifyPackage();
            if (game.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("游戏已加载 CoreCLR/BepInEx，不能再次初始化。本次未重复注入；如需更新运行库，请保存并重新启动游戏。");
            using var gate = new Mutex(false, "Local\\NightsHack.Install." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant()))));
            if (!gate.WaitOne(0)) throw new InvalidOperationException("另一个修改器正在部署，请稍后重试。");
            try
            {
                Deploy(manifest, progress);
                progress.Report(new("BOOTSTRAP_REQUESTED", $"目标 PID={game.Id}；发送一次性窗口线程加载请求。"));
                DynamicInjector.Inject(game, Path.Combine(package, "payload/bootstrap/NightsHack.Native.dll"), progress, requestId ?? Guid.NewGuid());
            }
            finally { gate.ReleaseMutex(); }
        }
        finally { running.ForEach(p => p.Dispose()); }
    }

    void Deploy(Dictionary<string, string> manifest, IProgress<InjectorEvent> progress)
    {
        var plan = new List<(string Source, string Destination, string Hash)>();
        foreach (var (source, hash) in manifest)
        {
            string? target = source.StartsWith("payload/hooks/", StringComparison.Ordinal)
                ? "BepInEx/plugins/NightsHack/" + source[14..]
                : source.StartsWith("payload/runtime/", StringComparison.Ordinal) ? source[16..] : null;
            if (target == null) continue;
            if (target.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) || target.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase) || target.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("发布包包含已停用的静态启动文件，部署已拒绝。");
            var destination = Under(root, target);
            if (File.Exists(destination) && Hash(destination) == hash) continue;
            // Other loaders/runtime versions are not silently replaced. Our own plugins can be updated with a backup.
            if (File.Exists(destination) && !target.StartsWith("BepInEx/plugins/NightsHack/", StringComparison.Ordinal))
                throw new InvalidOperationException("已有加载器文件与发布包不同，未覆盖：" + target);
            plan.Add((Under(package, source), destination, hash));
        }
        // Refuse duplicates in a different plugin folder, which BepInEx would also discover.
        string plugins = Path.Combine(root, "BepInEx/plugins");
        if (Directory.Exists(plugins))
            foreach (var file in Directory.EnumerateFiles(plugins, "*.dll", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                if (PluginNames.Contains(Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)!), Path.GetFullPath(Path.Combine(plugins, "NightsHack")), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("发现其他目录中的重复钩子，请先处理：" + file);
        progress.Report(new("DEPLOY_BEGIN", $"待部署文件数={plan.Count}；现有配置保留，更新文件保存备份。"));
        var completed = new List<(string Destination, string? Backup)>();
        string backupRoot = Path.Combine(root, "NightsHack-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var item in plan.OrderBy(p => Path.GetFileName(p.Destination).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                var running = Running();
                try
                {

                    if (running.Any(p => p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))))
                        throw new IOException("运行时已经加载，取消文件部署。");
                }
                finally { running.ForEach(p => p.Dispose()); }
                string? backup = null;
                if (File.Exists(item.Destination))
                {
                    backup = Under(backupRoot, Path.GetRelativePath(root, item.Destination));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(item.Destination, backup, false);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                completed.Add((item.Destination, backup));
                File.Copy(item.Source, item.Destination, true);
                if (Hash(item.Destination) != item.Hash) throw new IOException("部署后校验失败：" + item.Destination);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<string>();
            foreach (var item in completed.AsEnumerable().Reverse())
                try { if (item.Backup == null) File.Delete(item.Destination); else File.Copy(item.Backup, item.Destination, true); }
                catch (Exception e) { rollbackErrors.Add(item.Destination + ": " + e.Message); }
            if (rollbackErrors.Count > 0) throw new IOException(failure.Message + "；回滚未完成：" + string.Join("；", rollbackErrors), failure);
            throw;
        }
    }
}
