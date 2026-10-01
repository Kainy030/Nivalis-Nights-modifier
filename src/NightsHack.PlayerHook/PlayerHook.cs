using BepInEx;
using HarmonyLib;
using NightsHack.HookRuntime;
using System.Reflection;
using System.Text.Json;

namespace NightsHack.PlayerHook;

[BepInPlugin(PluginId, "PlayerHook", "0.1.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class PlayerHook : ObservationPlugin
{
    public const string PluginId = "nightshack.playerhook";
    public static PlayerHook? Active => FindActive(PluginId) as PlayerHook;
    protected override string Identifier => PluginId;
    protected override string CatalogResource => "PlayerHook.Catalog.json";
    protected override bool CapturePlayerDetails => true;
    internal CommandRuntime Commands { get; } = new();
    internal int LastGameThreadId { get; private set; }
    internal string? LastPlayerProbe { get; private set; }
    CommandPipeServer? commandPipe;
    long lastDispatchAudit;

    public override void Load()
    {
        base.Load();
        HookAudit.CommandEventSink = record =>
        {
            string line = $"[{record.Code}] request={record.RequestId} target={record.Target} operation={record.Operation} outcome={record.Outcome}; {record.Message}";
            if (record.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) Log.LogError(line);
            else if (record.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase)) Log.LogWarning(line);
            else Log.LogInfo(line);
        };
        Commands.Register("Nivalis.PlayerManager.LocalPlayer.Inventory", ProbePlayerInventory);
        Commands.Register("Nivalis.PlayerManager.LocalPlayer.Inventory.Money", SetMoney);
        if (!Config.Bind("Commands", "Enabled", false,
            "Experimental command dispatcher; requires explicit opt-in and is disabled by default.").Value) return;
        try
        {
            var managerUpdate = ResolveTarget("Nivalis.PlayerManager.Update()");
            new Harmony("nightshack.playerhook.commands").Patch(managerUpdate,
                postfix: new HarmonyMethod(typeof(PlayerHook), nameof(DrainCommands)));
            commandPipe = new CommandPipeServer("NightsHack.Command." + Environment.ProcessId, Commands);
            commandPipe.Start();
            HookAudit.Lifecycle(Identifier, "COMMAND_DISPATCH_READY", "Ready", "Experimental game-thread command dispatcher enabled.");
        }
        catch (Exception error)
        {
            HookAudit.Lifecycle(Identifier, "COMMAND_DISPATCH_FAILED", "Disabled", error.GetBaseException().Message, "WARN");
        }
    }

    static void DrainCommands() => Active?.DrainOnGameThread();
    void DrainOnGameThread()
    {
        LastGameThreadId = Environment.CurrentManagedThreadId;
        Commands.Drain();
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastDispatchAudit) >= 1000 && Interlocked.Exchange(ref lastDispatchAudit, now) <= now - 1000)
            HookAudit.Lifecycle(Identifier, "COMMAND_TICK", "Running", $"gameThread={LastGameThreadId}; pending={Commands.PendingCount}");
    }

    CommandResponse ProbePlayerInventory(CommandRequest request)
    {
        HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "COMMAND_RECEIVED", "AcceptedForValidation", "Command received over local IPC.");
        HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "HOOK_CALL_BEGIN", "Executing", "Resolving current player inventory on game thread.");
        try
        {
            var manager = FindSingleton("Nivalis.PlayerManager");
            var player = GetProperty(manager, "LocalPlayer");
            var inventory = GetProperty(player, "Inventory");
            var money = GetProperty(inventory, "Money");
            LastPlayerProbe = $"Resolved {manager.GetType().FullName} -> {player?.GetType().FullName} -> {inventory?.GetType().FullName}; money={money}";
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "PLAYER_PROBE", "Resolved", LastPlayerProbe);
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "COMMAND_COMPLETED", "Succeeded", LastPlayerProbe);
            return new(request.RequestId, "Completed", true, LastPlayerProbe,
                JsonSerializer.Serialize(new { managerType = manager.GetType().FullName, playerType = player?.GetType().FullName, inventoryType = inventory?.GetType().FullName, money, gameThread = LastGameThreadId }));
        }
        catch (Exception error)
        {
            LastPlayerProbe = error.GetBaseException().Message;
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "COMMAND_FAILED", "NotExecuted", LastPlayerProbe, "WARN");
            return new(request.RequestId, "Unavailable", false, LastPlayerProbe, "{}");
        }
    }

    CommandResponse SetMoney(CommandRequest request)
    {
        HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "COMMAND_RECEIVED", "AcceptedForValidation", "Command received over local IPC.");
        HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "HOOK_CALL_BEGIN", "Executing", "Money mutation dispatched on game thread.");
        try
        {
            using var args = JsonDocument.Parse(request.ArgumentsJson);
            if (!args.RootElement.TryGetProperty("value", out var valueElement) || !valueElement.TryGetInt32(out var desired) || desired <= 0 || desired > int.MaxValue - 1)
            {
                const string message = "value must be an Int32 in the safe game-money range 1..2147483646.";
                HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation, "COMMAND_REJECTED", "NotExecuted", message, "WARN");
                return new(request.RequestId, "Rejected", false, message, "{}");
            }
            var manager = FindSingleton("Nivalis.PlayerManager");
            var inventory = GetProperty(GetProperty(manager, "LocalPlayer"), "Inventory") ?? throw new InvalidOperationException("Local player inventory is null.");
            var property = inventory.GetType().GetProperty("Money", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Money property is unavailable.");
            var before = Convert.ToInt32(property.GetValue(inventory));
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation,
                "HOOK_TARGET_READY", "Validated", $"before={before}; target={desired}; modifierTarget={desired / 100m:0.00}.");
            property.SetValue(inventory, desired);
            var after = Convert.ToInt32(property.GetValue(inventory));
            bool changed = after == desired;
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation,
                "MONEY_SET", changed ? "Succeeded" : "SideEffectsUnknown", $"before={before}; target={desired}; after={after}; modifierTarget={desired / 100m:0.00}", changed ? "INFO" : "WARN");
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation,
                "COMMAND_COMPLETED", changed ? "Succeeded" : "SideEffectsUnknown", $"before={before}; target={desired}; after={after}", changed ? "INFO" : "WARN");
            return new(request.RequestId, changed ? "Completed" : "SideEffectsUnknown", changed, $"Money set from {before} to {after}.", JsonSerializer.Serialize(new { before, value = desired, after, gameThread = LastGameThreadId }));
        }
        catch (Exception error)
        {
            HookAudit.CommandEvent(request.RequestId, request.Feature, Identifier, request.TargetId, request.Operation,
                "COMMAND_FAILED", "SideEffectsUnknown", error.GetBaseException().Message, "WARN");
            return new(request.RequestId, "SideEffectsUnknown", false, error.GetBaseException().Message, "{}");
        }
    }

    object FindSingleton(string fullName)
    {
        var type = ResolveRuntimeType(fullName) ?? throw new InvalidOperationException($"Runtime type not found: {fullName}");
        var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
        return instance ?? throw new InvalidOperationException($"Singleton instance is null: {fullName}");
    }

    static object? GetProperty(object? value, string name) => value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

    static Type? ResolveRuntimeType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t is not null);
}
