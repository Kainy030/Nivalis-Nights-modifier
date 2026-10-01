using NightsHack.Logging;

namespace NightsHack.HookRuntime;

public sealed record HookCommandResult<T>(bool Succeeded, T Value, string Summary);

/// <summary>Mandatory entry point for future trainer feature execution. Does not dispatch threads or implement gameplay.</summary>
public static class HookAudit
{
    static readonly object Gate = new();
    static EventJournal? journal;
    /// <summary>Optional game-side sink for command events. The PlayerHook binds this to BepInEx logging.</summary>
    public static Action<AuditRecord>? CommandEventSink { get; set; }
    public static string? LogPath => journal?.Path;
    public static void Initialize(string logDirectory)
    {
        lock (Gate)
        {
            if (journal != null) return;
            journal = new EventJournal(Path.Combine(logDirectory, "hooks-" + LogSession.Current + ".jsonl"));
            Write(new AuditRecord { Component = "HookRuntime", Code = "HOOK_SESSION_STARTED", Operation = "InitializeAudit", Outcome = "Ready", Message = "Event-driven hook audit initialized; no periodic observations enabled." });
        }
    }
    public static void Lifecycle(string hook, string code, string outcome, string message, string level = "INFO") =>
        Write(new AuditRecord { Component = "HookRuntime", Hook = hook, Code = code, Level = level, Outcome = outcome,
            RequestId = Environment.GetEnvironmentVariable("NIGHTSHACK_REQUEST_ID") ?? "", Operation = "PluginLifecycle", Message = message });

    public static void CommandEvent(Guid requestId, string feature, string hook, string target, string operation, string code, string outcome, string message, string level = "INFO")
    {
        var record = new AuditRecord { Component = "HookRuntime", RequestId = requestId.ToString("D"), Feature = feature, Hook = hook,
            Target = target, Operation = operation, Code = code, Outcome = outcome, Message = message, Level = level };
        Write(record);
        try { CommandEventSink?.Invoke(record); } catch { }
    }

    static void Write(AuditRecord record) => (journal ?? throw new InvalidOperationException("Hook audit is unavailable; execution is refused.")).Write(record);

    // Preparation (including thread/instance/target validation) precedes HOOK_CALL_BEGIN.
    // A return value must explicitly report success/failure; returning from a delegate alone is not success.
    public static HookCommandResult<T> Execute<T>(Guid requestId, string feature, string hook, string target, string operation,
        Func<Func<HookCommandResult<T>>> prepare)
    {
        if (requestId == Guid.Empty || new[] { feature, hook, target, operation }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A request ID and explicit feature/hook/target/operation are required.");
        var context = new AuditRecord { RequestId = requestId.ToString("D"), Feature = feature, Hook = hook,
            Target = target, Operation = operation, Component = "HookRuntime" };
        void Record(string code, string outcome, string message, string level = "INFO") => Write(context with { TimestampUtc = DateTimeOffset.UtcNow, Code = code, Outcome = outcome, Message = message, Level = level });
        // Audit failure before execution prevents the operation from running.
        Record("COMMAND_RECEIVED", "AcceptedForValidation", "Feature request received; gameplay has not been executed.");
        bool invoked = false;
        try
        {
            var invoke = prepare() ?? throw new InvalidOperationException("No validated operation was provided.");
            Record("HOOK_TARGET_READY", "Validated", "Target and execution context validation completed.");
            Record("HOOK_CALL_BEGIN", "Executing", "Invoking the explicitly requested feature operation.");
            invoked = true;
            var result = invoke();
            Record("HOOK_CALL_END", result.Succeeded ? "Succeeded" : "Rejected", result.Summary, result.Succeeded ? "INFO" : "WARN");
            Record("COMMAND_COMPLETED", result.Succeeded ? "Succeeded" : "Rejected", result.Summary, result.Succeeded ? "INFO" : "WARN");
            return result;
        }
        catch (Exception error)
        {
            try { Record("COMMAND_FAILED", invoked ? "SideEffectsUnknown" : "NotExecuted", error.GetBaseException().ToString(), "ERROR"); }
            catch (Exception auditError) { throw new AggregateException("Command/audit failed. Do not automatically retry; inspect potential side effects.", error, auditError); }
            throw;
        }
    }
}
