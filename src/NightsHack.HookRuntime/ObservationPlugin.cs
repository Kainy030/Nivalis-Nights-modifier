using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;

namespace NightsHack.HookRuntime;

/// <summary>Shared observation engine. It never invokes game business methods or writes game fields.</summary>
public abstract class ObservationPlugin : BasePlugin
{
    protected abstract string Identifier { get; }
    protected abstract string CatalogResource { get; }
    protected virtual bool CapturePlayerDetails => false;
    private static readonly ConcurrentDictionary<string, ObservationPlugin> ActivePlugins = new();
    private static readonly ConcurrentDictionary<MethodBase, Endpoint> Dispatch = new();
    protected static ObservationPlugin? FindActive(string id) => ActivePlugins.TryGetValue(id, out var plugin) ? plugin : null;
    public HookObservationBuffer Observations { get; } = new();
    public bool Installed => Volatile.Read(ref installed);
    public IReadOnlyList<string> AvailableTargetIds => availableTargetIds;
    public IReadOnlyList<string> FieldDiagnostics => snapshots?.Diagnostics ?? Array.Empty<string>();
    private bool installed;
    private Catalog? catalog;
    private IReadOnlyList<string> availableTargetIds = Array.Empty<string>();
    private Assembly? gameAssembly;
    private long moduleBase;
    private int loaderThreadId;
    private long diagnosticDeadline;
    private ObservationBudget? traceBudget;
    private bool captureFields, capturePlayerDetails;
    private System.Threading.Timer? diagnosticsTimer;
    private readonly object diagnosticsLock = new();
    private long callSequence;
    private NativeSnapshots? snapshots;
    private Endpoint[] endpoints = Array.Empty<Endpoint>();
    private IReadOnlyList<HookStatus> exclusions = Array.Empty<HookStatus>();
    private readonly List<Harmony> patches = new();

    private sealed class Endpoint
    {
        internal readonly ObservationPlugin Owner;
        internal readonly MethodSpec Spec;
        internal readonly SampleGate Gate;
        internal readonly ObservationMode Mode;
        internal readonly bool Diagnostic;
        internal readonly string Id;
        internal MethodInfo? Method;
        internal string State = "Pending", Detail = "";
        internal long Calls, Samples, Faults;
        internal Endpoint(ObservationPlugin owner, MethodSpec spec, ObservationMode mode, bool diagnostic, int interval)
        { Owner = owner; Spec = spec; Mode = mode; Diagnostic = diagnostic; Id = spec.Id; Gate = new SampleGate(interval); }
        internal HookStatus Status()
        {
            long calls = Interlocked.Read(ref Calls), samples = Interlocked.Read(ref Samples), faults = Interlocked.Read(ref Faults);
            string state = State == "Installed" && Diagnostic && Owner.DiagnosticsExpired ? "DiagnosticExpired" :
                State == "Installed" && Mode == ObservationMode.Trace && Owner.traceBudget?.IsExhausted == true ? "BudgetExhausted" :
                State == "Installed" && faults >= 3 ? "ObservationDisabled" :
                State == "Installed" && calls > 0 ? "Observed" : State;
            string detail = state == "Observed" && faults == 0
                ? "Callback observed; inspect samples and field diagnostics for actual data coverage." : Detail;
            return new(Id, Spec.Group, state, detail, calls, samples, faults);
        }
    }

    public IReadOnlyList<HookStatus> GetStatus() => Array.AsReadOnly(endpoints.Select(e => e.Status()).Concat(exclusions).ToArray());

    public override void Load()
    {
        HookAudit.Initialize(Path.Combine(Paths.BepInExRootPath, "logs", "NightsHack"));
        HookAudit.Lifecycle(Identifier, "HOOK_LOAD_BEGIN", "Loading", "Initializing passive target registry.");
        if (!Config.Bind("Hook", "Enabled", true, "Enable the passive target registry; does not opt into observation.").Value)
        { HookAudit.Lifecycle(Identifier, "HOOK_DISABLED", "Disabled", "Plugin disabled by explicit configuration."); return; }
        if (!ActivePlugins.TryAdd(Identifier, this)) { Log.LogWarning(Identifier + " already active."); return; }
        try
        {
            loaderThreadId = Environment.CurrentManagedThreadId;
            catalog = Catalog.Load(GetType().Assembly, CatalogResource);
            availableTargetIds = Array.AsReadOnly(catalog.Methods.Select(m => m.Id).ToArray());
            string features = Config.Bind("Features", "AllowList", "",
                "Explicit full method IDs separated by semicolons; empty installs no feature observers. Selected feature observers count only.").Value;
            bool diagnosticsEnabled = Config.Bind("Diagnostics", "Enabled", false,
                "Explicitly opt into selected diagnostics. Legacy Groups/ExportIntervalSeconds never enable observation. Restart required.").Value;
            string diagnosticMode = Config.Bind("Diagnostics", "Mode", "Counter", "Counter or Trace; only used when Diagnostics.Enabled=true.").Value;
            string diagnosticAllowList = Config.Bind("Diagnostics", "AllowList", "", "Explicit full method IDs separated by semicolons; no wildcards or groups.").Value;
            var selection = ObservationPolicy.Select(catalog, features, diagnosticsEnabled, diagnosticMode, diagnosticAllowList);
            exclusions = Array.AsReadOnly(catalog.Excluded.Select(e => new HookStatus(
                $"{e.Type}.{e.Name}@{e.Rva}", "Excluded", "Excluded", e.Reason, 0, 0, 0)).ToArray());
            if (selection.Length == 0)
            {
                HookAudit.Lifecycle(Identifier, "HOOK_READY", "Passive", $"targets={catalog.Methods.Length}; observationPatches=0; snapshots=false; exportTimer=false");
                Log.LogInfo($"{Identifier}: passive registry ready; {catalog.Methods.Length} available targets, 0 observation patches, no snapshots or export timer. AI/render targets are metadata only.");
                return;
            }
            var diagnosticIds = ObservationPolicy.Select(catalog, "", diagnosticsEnabled, diagnosticMode, diagnosticAllowList)
                .Select(s => s.Method.Id).ToHashSet(StringComparer.Ordinal);
            int interval = Math.Clamp(Config.Bind("Observation", "SampleIntervalMs", 250,
                "Explicit Trace endpoints only; applies to EVERY selected trace method (25..10000 ms), across instances.").Value, 25, 10000);
            endpoints = selection.Select(s => new Endpoint(this, s.Method, s.Mode, diagnosticIds.Contains(s.Method.Id), interval)).ToArray();
            int duration = Math.Clamp(Config.Bind("Diagnostics", "DurationSeconds", 30, "Diagnostic collection lifetime, 1..300 seconds. Detours remain until restart/unload.").Value, 1, 300);
            bool hasTrace = selection.Any(s => s.Mode == ObservationMode.Trace);
            captureFields = hasTrace && Config.Bind("Diagnostics", "CaptureFields", false, "Explicit Trace only: include instance fields; otherwise args/result only.").Value;
            capturePlayerDetails = captureFields && Config.Bind("Diagnostics", "CapturePlayerDetails", false, "Explicit Trace only: expand player stats/skills/locks.").Value;
            if (hasTrace)
                traceBudget = new ObservationBudget(
                    Math.Clamp(Config.Bind("Diagnostics", "MaxSamplesPerSecond", 20, "Shared per-plugin Trace budget, 1..200 captures; each admits a Before/After pair.").Value, 1, 200),
                    Math.Clamp(Config.Bind("Diagnostics", "MaxTotalSamples", 500, "Total Trace capture budget, 1..5000; each admits a Before/After pair.").Value, 1, 5000), duration);
            EnsureNativeContext();
            if (captureFields) snapshots = new NativeSnapshots(gameAssembly!, SelectSnapshotTypes(catalog, selection));
            var excludedRvas = Config.Bind("Diagnostics", "ExcludedRvas", "",
                "Temporary isolation only: comma-separated catalog RVAs to leave unpatched. Empty enables the full configured scope.").Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < endpoints.Length; i++)
            {
                var endpoint = endpoints[i];
                if (excludedRvas.Contains(endpoint.Spec.Rva))
                { endpoint.State = "Disabled"; endpoint.Detail = "Explicit diagnostic isolation override."; continue; }
                if (!Config.Bind("Groups", endpoint.Spec.Group, true, "Observe this subsystem type.").Value)
                { endpoint.State = "Disabled"; continue; }
                try
                {
                    endpoint.Method = ResolveTarget(endpoint.Id);
                    TargetValidation.VerifyKnownNativeCompatibility(endpoint.Spec);
                    if (!Dispatch.TryAdd(endpoint.Method, endpoint)) throw new InvalidOperationException("Method already owned by another observer.");
                }
                catch (Exception error)
                { endpoint.State = "Rejected"; endpoint.Detail = error.GetBaseException().Message; continue; }
                var harmony = new Harmony(Identifier + "." + i);
                patches.Add(harmony);
                try
                {
                    bool isStatic = endpoint.Spec.IsStatic, returnsVoid = endpoint.Method!.ReturnType == typeof(void);
                    string before = isStatic ? nameof(BeforeStatic) : nameof(BeforeInstance);
                    string after = isStatic ? (returnsVoid ? nameof(AfterStaticVoid) : nameof(AfterStaticResult)) :
                        (returnsVoid ? nameof(AfterInstanceVoid) : nameof(AfterInstanceResult));
                    if (endpoint.Mode == ObservationMode.Counter)
                        harmony.Patch(endpoint.Method, prefix: new HarmonyMethod(typeof(ObservationPlugin), nameof(CounterOnly)));
                    else
                        harmony.Patch(endpoint.Method, prefix: new HarmonyMethod(typeof(ObservationPlugin), before),
                            postfix: new HarmonyMethod(typeof(ObservationPlugin), after));
                    endpoint.State = "Installed";
                    endpoint.Detail = "Patch accepted; native hits and field coverage not yet confirmed.";
                }
                catch (Exception error)
                {
                    endpoint.State = "Failed"; endpoint.Detail = error.GetBaseException().Message;
                    try { harmony.UnpatchSelf(); }
                    catch (Exception cleanup) { endpoint.Detail += "; cleanup failed: " + cleanup.Message; }
                }
            }
            diagnosticDeadline = Stopwatch.GetTimestamp() + (long)duration * Stopwatch.Frequency;
            Volatile.Write(ref installed, endpoints.Any(e => e.State == "Installed"));
            HookAudit.Lifecycle(Identifier, "HOOK_READY", "ExplicitObservation", $"Selected={endpoints.Length}; Installed={endpoints.Count(e => e.State == "Installed")}; diagnostics={diagnosticsEnabled}");
            Log.LogInfo($"{Identifier}: {endpoints.Count(e => e.State == "Installed")}/{endpoints.Length} installed; {catalog.Excluded.Length} excluded. Observation only.");
            foreach (var status in GetStatus().Where(s => s.State is "Failed" or "Rejected")) Log.LogWarning($"{status.Id}: {status.Detail}");
            foreach (string diagnostic in FieldDiagnostics) Log.LogWarning("Field unavailable: " + diagnostic);
            int exportSeconds = diagnosticsEnabled && diagnosticIds.Count > 0 && Installed ? Config.Bind("Diagnostics", "ExportIntervalSeconds", 0,
                "Write detached status/latest samples to BepInEx/diagnostics; 0 disables, otherwise minimum 5 seconds.").Value
                : 0;
            if (exportSeconds > 0)
            {
                var exportInterval = TimeSpan.FromSeconds(Math.Clamp(exportSeconds, 5, 3600));
                // Even a short diagnostic session emits its terminal report.
                var timerInterval = TimeSpan.FromSeconds(Math.Min(exportInterval.TotalSeconds, duration));
                diagnosticsTimer = new System.Threading.Timer(_ => ExportDiagnostics(), null, timerInterval, timerInterval);
            }
        }
        catch (Exception error) { Log.LogError(Identifier + " installation refused/failed: " + error); HookAudit.Lifecycle(Identifier, "HOOK_LOAD_FAILED", "Failed", error.ToString(), "ERROR"); Unload(); }
    }

    private bool DiagnosticsExpired => Stopwatch.GetTimestamp() >= diagnosticDeadline;

    /// <summary>Future feature entry: logs request, validated target, invocation and explicit outcome.
    /// Caller must supply its feature-specific instance/state validation and remain on the loader/game thread.</summary>
    public HookCommandResult<T> ExecuteCommand<T>(Guid requestId, string feature, string operation, string targetId,
        Func<MethodInfo, HookCommandResult<T>> execute) =>
        HookAudit.Execute<T>(requestId, feature, Identifier, targetId, operation, () =>
        {
            var target = ResolveTarget(targetId);
            return () => execute(target);
        });

    // Resolution is deliberately separate from interception. It never installs a
    // Harmony patch, executes a method, or treats a historical pointer as an instance.
    // Callers must separately validate gameplay state and marshal execution correctly.
    public MethodInfo ResolveTarget(string targetId)
    {
        if (catalog == null || Environment.CurrentManagedThreadId != loaderThreadId)
            throw new InvalidOperationException("Target resolution requires a loaded plugin and its loader thread; no automatic thread dispatch is implemented.");
        var spec = catalog.Methods.SingleOrDefault(m => m.Id == targetId)
            ?? throw new InvalidOperationException("Unknown or excluded target ID.");
        EnsureNativeContext();
        Type type = NativeSnapshots.ResolveType(gameAssembly!, spec.Type);
        if (!spec.IsStatic && !typeof(Il2CppObjectBase).IsAssignableFrom(type))
            throw new InvalidOperationException("Instance is not generated object interop.");
        IntPtr klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
        if (klass == IntPtr.Zero) throw new InvalidOperationException("No native class.");
        MethodInfo method = TargetValidation.Resolve(type, spec, NativeSnapshots.NativeName(klass));
        bool IsNativeValueType(Type element)
        {
            if (!typeof(Il2CppObjectBase).IsAssignableFrom(element)) return false;
            IntPtr elementClass = Il2CppClassPointerStore.GetNativeClassPointer(element);
            if (elementClass == IntPtr.Zero) throw new InvalidOperationException("Missing native class.");
            return IL2CPP.il2cpp_class_is_valuetype(elementClass);
        }
        TargetValidation.VerifyByRefMarshalling(method);
        TargetValidation.VerifyReturnMarshalling(method, IsNativeValueType);
        TargetValidation.VerifySmallValueParameters(method, element =>
        {
            if (!IsNativeValueType(element)) return null;
            uint alignment = 0;
            return IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore.GetNativeClassPointer(element), ref alignment);
        });
        VerifyNativeEntry(method, moduleBase, spec.Rva);
        return method;
    }

    private void EnsureNativeContext()
    {
        if (gameAssembly != null) return;
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("x64 required.");
        string assemblyPath = Path.Combine(Paths.GameRootPath, "GameAssembly.dll");
        string metadataPath = Path.Combine(Paths.GameRootPath, "Nivalis Nights_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        if (!TargetValidation.TryVerifyFile(assemblyPath, TargetValidation.AssemblyHash, out string assemblyWarning))
            Log.LogWarning($"{Identifier}: 游戏版本/程序集校验不匹配，继续尝试运行；部分钩子功能可能不可用。{assemblyWarning}");
        if (!TargetValidation.TryVerifyFile(metadataPath, TargetValidation.MetadataHash, out string metadataWarning))
            Log.LogWarning($"{Identifier}: 游戏 metadata 版本校验不匹配，继续尝试运行；部分钩子功能可能不可用。{metadataWarning}");
        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        var assembly = Assembly.Load(new AssemblyName("Assembly-CSharp"));
        moduleBase = module.BaseAddress.ToInt64();
        gameAssembly = assembly;
    }

    private IEnumerable<TypeSpec> SelectSnapshotTypes(Catalog source, IEnumerable<ObservationSelection> selection)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selected in selection.Where(s => s.Mode == ObservationMode.Trace))
        {
            for (Type? type = NativeSnapshots.ResolveType(gameAssembly!, selected.Method.Type); type != null; type = type.BaseType)
                names.Add(TargetValidation.Canonical(type));
        }
        if (capturePlayerDetails && CapturePlayerDetails)
            foreach (var type in source.Types.Where(t => t.Name.StartsWith("Nivalis.Player", StringComparison.Ordinal) ||
                t.Name.StartsWith("Nivalis.SkillSystem.", StringComparison.Ordinal) || t.Name.StartsWith("Nivalis.OverrideableBool", StringComparison.Ordinal)))
                names.Add(type.Name);
        return source.Types.Where(t => names.Contains(t.Name));
    }

    private static unsafe void VerifyNativeEntry(MethodInfo method, long moduleBase, string rva)
    {
        FieldInfo field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method)
            ?? throw new InvalidOperationException("Generated native MethodInfo pointer missing.");
        if (field.GetValue(null) is not IntPtr pointer || pointer == IntPtr.Zero)
            throw new InvalidOperationException("Null native MethodInfo.");
        IntPtr entry = UnityVersionHandler.Wrap((Il2CppMethodInfo*)pointer).MethodPointer;
        long actual = entry.ToInt64() - moduleBase;
        if (actual != Convert.ToInt64(rva, 16))
            throw new InvalidOperationException($"Native entry differs: actual 0x{actual:X}, expected {rva}.");
    }

    public override bool Unload()
    {
        diagnosticsTimer?.Dispose();
        diagnosticsTimer = null;
        Volatile.Write(ref installed, false);
        bool success = true;
        foreach (var harmony in patches)
        {
            try { harmony.UnpatchSelf(); }
            catch (Exception error) { success = false; Log.LogError(Identifier + " unpatch failed: " + error); }
        }
        foreach (var endpoint in endpoints)
        {
            if (endpoint.State == "Installed") endpoint.State = success ? "Unloaded" : "CleanupUncertain";
            // Retain dispatch ownership when unpatching failed; reject an unsafe second installation.
            if (success && endpoint.Method != null && Dispatch.TryGetValue(endpoint.Method, out var current) && ReferenceEquals(current, endpoint))
                Dispatch.TryRemove(endpoint.Method, out _);
        }
        if (success)
        {
            patches.Clear();
            if (ReferenceEquals(FindActive(Identifier), this)) ActivePlugins.TryRemove(Identifier, out _);
            catalog = null;
            availableTargetIds = Array.Empty<string>();
            snapshots = null;
            gameAssembly = null;
        }
        HookAudit.Lifecycle(Identifier, "HOOK_UNLOAD", success ? "Unloaded" : "CleanupUncertain", "Plugin unload completed.", success ? "INFO" : "WARN");
        return success;
    }

    private void ExportDiagnostics()
    {
        if (!Monitor.TryEnter(diagnosticsLock)) return;
        bool finalReport = DiagnosticsExpired;
        try
        {
            // Read only managed, detached observations; never access game objects on this timer.
            string directory = Path.Combine(Paths.BepInExRootPath, "diagnostics");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Identifier + ".json");
            var report = new
            {
                TimestampUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId,
                Plugin = Identifier, Installed, Status = GetStatus(),
                Dropped = Observations.DroppedCount, Latest = Observations.GetLatest()
            };
            File.WriteAllText(destination + ".tmp", System.Text.Json.JsonSerializer.Serialize(report));
            File.Move(destination + ".tmp", destination, true);
        }
        catch (Exception error) { Log.LogWarning(Identifier + " diagnostic export failed: " + error.Message); }
        finally
        {
            if (finalReport) { diagnosticsTimer?.Dispose(); diagnosticsTimer = null; }
            Monitor.Exit(diagnosticsLock);
        }
    }

    private sealed record Capture(Endpoint Endpoint, long CallId, long InstanceAddress);
    private static void CounterOnly(MethodBase __originalMethod)
    {
        if (!Dispatch.TryGetValue(__originalMethod, out var endpoint) || endpoint.Mode != ObservationMode.Counter ||
            !endpoint.Owner.Installed || endpoint.State != "Installed" ||
            (endpoint.Diagnostic && endpoint.Owner.DiagnosticsExpired)) return;
        Interlocked.Increment(ref endpoint.Calls);
    }
    private static void BeforeInstance(Il2CppObjectBase __instance, MethodBase __originalMethod, object[] __args, out Capture? __state)
        => Begin(__instance, __originalMethod, __args, out __state);
    private static void BeforeStatic(MethodBase __originalMethod, object[] __args, out Capture? __state)
        => Begin(null, __originalMethod, __args, out __state);
    private static void Begin(Il2CppObjectBase? instance, MethodBase method, object[] args, out Capture? state)
    {
        state = null;
        if (!Dispatch.TryGetValue(method, out var endpoint)) return;
        var owner = endpoint.Owner;
        if (!owner.Installed || endpoint.State != "Installed" || endpoint.Mode != ObservationMode.Trace || owner.DiagnosticsExpired) return;
        Interlocked.Increment(ref endpoint.Calls);
        long now = Stopwatch.GetTimestamp();
        if (Interlocked.Read(ref endpoint.Faults) >= 3 || !endpoint.Gate.TryEnter(now) || owner.traceBudget?.TryEnter(now) != true) return;
        try
        {
            long id = Interlocked.Increment(ref owner.callSequence), address = instance?.Pointer.ToInt64() ?? 0;
            owner.CaptureValues(instance, args, endpoint, id, address, "Before", null);
            Interlocked.Increment(ref endpoint.Samples);
            state = new Capture(endpoint, id, address);
        }
        catch (Exception error) { owner.Fault(endpoint, error); }
    }

    private static void AfterInstanceVoid(Il2CppObjectBase __instance, object[] __args, Capture? __state) => Finish(__instance, __args, null, __state);
    private static void AfterInstanceResult(Il2CppObjectBase __instance, object[] __args, object? __result, Capture? __state) => Finish(__instance, __args, __result, __state);
    private static void AfterStaticVoid(object[] __args, Capture? __state) => Finish(null, __args, null, __state);
    private static void AfterStaticResult(object[] __args, object? __result, Capture? __state) => Finish(null, __args, __result, __state);
    private static void Finish(Il2CppObjectBase? instance, object[] args, object? result, Capture? state)
    {
        if (state == null || !state.Endpoint.Owner.Installed || state.Endpoint.State != "Installed") return;
        try { state.Endpoint.Owner.CaptureValues(instance, args, state.Endpoint, state.CallId, state.InstanceAddress, "After", result); }
        catch (Exception error) { state.Endpoint.Owner.Fault(state.Endpoint, error); }
    }

    private void CaptureValues(Il2CppObjectBase? instance, object[] args, Endpoint endpoint, long callId, long address, string phase, object? result)
    {
        var values = new List<ObservedValue>();
        bool skipAfter = endpoint.Spec.SkipAfterInstance || endpoint.Spec.Name.Contains("Destroy", StringComparison.Ordinal);
        if (captureFields && instance != null && (phase == "Before" || !skipAfter))
        {
            values.AddRange(snapshots!.Read(instance));
            if (capturePlayerDetails && CapturePlayerDetails)
            {
                values.AddRange(snapshots.ReadStatEntries(instance));
                values.AddRange(snapshots.ReadOwnedLocks(instance));
            }
        }
        if (snapshots != null) values.AddRange(snapshots.Arguments(args, endpoint.Spec, phase == "Before", capturePlayerDetails && CapturePlayerDetails));
        else
            for (int i = 0; i < args.Length && i < endpoint.Spec.Parameters.Length; i++)
                values.Add(NativeSnapshots.Describe("arg." + endpoint.Spec.ParameterNames[i], args[i]));
        if (phase == "After" && endpoint.Spec.ReturnType != "System.Void")
        {
            values.Add(NativeSnapshots.Describe("result", result));
            if (capturePlayerDetails && CapturePlayerDetails && endpoint.Spec.ReturnType == "Nivalis.OverrideableBool+OverrideLock" && result is Il2CppObjectBase native)
                values.AddRange(snapshots!.Read(native, "result."));
        }
        Observations.Record(callId, phase, endpoint.Spec, address, values, endpoint.Id);
    }

    private void Fault(Endpoint endpoint, Exception error)
    {
        if (Interlocked.Increment(ref endpoint.Faults) != 1) return;
        try
        {
            endpoint.Detail = "Observer fault: " + error.GetBaseException().Message;
            Log.LogWarning($"{endpoint.Spec.Id}: {endpoint.Detail}; sampling stops after 3 faults.");
        }
        catch { /* Observer diagnostics must not escape into game logic. */ }
    }
}
