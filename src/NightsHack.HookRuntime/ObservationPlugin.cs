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
    public IReadOnlyList<string> FieldDiagnostics => snapshots?.Diagnostics ?? Array.Empty<string>();
    private bool installed;
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
        internal MethodInfo? Method;
        internal string State = "Pending", Detail = "";
        internal long Calls, Samples, Faults;
        internal Endpoint(ObservationPlugin owner, MethodSpec spec, int interval)
        { Owner = owner; Spec = spec; Gate = new SampleGate(spec.Sampled ? interval : 0); }
        internal HookStatus Status()
        {
            long calls = Interlocked.Read(ref Calls), samples = Interlocked.Read(ref Samples), faults = Interlocked.Read(ref Faults);
            string state = State == "Installed" && faults >= 3 ? "ObservationDisabled" :
                State == "Installed" && calls > 0 ? "Observed" : State;
            string detail = state == "Observed" && faults == 0
                ? "Callback observed; inspect samples and field diagnostics for actual data coverage." : Detail;
            return new(Spec.Id, Spec.Group, state, detail, calls, samples, faults);
        }
    }

    public IReadOnlyList<HookStatus> GetStatus() => Array.AsReadOnly(endpoints.Select(e => e.Status()).Concat(exclusions).ToArray());

    public override void Load()
    {
        if (!Config.Bind("Hook", "Enabled", true, "Install observation-only hooks.").Value) return;
        if (!ActivePlugins.TryAdd(Identifier, this)) { Log.LogWarning(Identifier + " already active."); return; }
        try
        {
            if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("x64 required.");
            TargetValidation.VerifyFile(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"), TargetValidation.AssemblyHash);
            TargetValidation.VerifyFile(Path.Combine(Paths.GameRootPath, "Nivalis Nights_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), TargetValidation.MetadataHash);
            Catalog catalog = Catalog.Load(GetType().Assembly, CatalogResource);
            int interval = Math.Clamp(Config.Bind("Observation", "SampleIntervalMs", 250,
                "Frequent endpoints only, per method across instances (25..10000 ms).").Value, 25, 10000);
            endpoints = catalog.Methods.Select(m => new Endpoint(this, m, interval)).ToArray();
            exclusions = Array.AsReadOnly(catalog.Excluded.Select(e => new HookStatus(
                $"{e.Type}.{e.Name}@{e.Rva}", "Excluded", "Excluded", e.Reason, 0, 0, 0)).ToArray());
            var assembly = Assembly.Load(new AssemblyName("Assembly-CSharp"));
            snapshots = new NativeSnapshots(assembly, catalog.Types);
            using var process = Process.GetCurrentProcess();
            var module = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            long moduleBase = module.BaseAddress.ToInt64();
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
                    Type type = NativeSnapshots.ResolveType(assembly, endpoint.Spec.Type);
                    if (!endpoint.Spec.IsStatic && !typeof(Il2CppObjectBase).IsAssignableFrom(type))
                        throw new InvalidOperationException("Instance is not generated object interop.");
                    IntPtr klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
                    if (klass == IntPtr.Zero) throw new InvalidOperationException("No native class.");
                    endpoint.Method = TargetValidation.Resolve(type, endpoint.Spec, NativeSnapshots.NativeName(klass));
                    bool IsNativeValueType(Type element)
                    {
                        if (!typeof(Il2CppObjectBase).IsAssignableFrom(element)) return false;
                        IntPtr elementClass = Il2CppClassPointerStore.GetNativeClassPointer(element);
                        if (elementClass == IntPtr.Zero) throw new InvalidOperationException("Missing byref native class.");
                        return IL2CPP.il2cpp_class_is_valuetype(elementClass);
                    }
                    TargetValidation.VerifyByRefMarshalling(endpoint.Method);
                    TargetValidation.VerifyReturnMarshalling(endpoint.Method, IsNativeValueType);
                    TargetValidation.VerifySmallValueParameters(endpoint.Method, element =>
                    {
                        if (!IsNativeValueType(element)) return null;
                        uint alignment = 0;
                        return IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore.GetNativeClassPointer(element), ref alignment);
                    });
                    VerifyNativeEntry(endpoint.Method, moduleBase, endpoint.Spec.Rva);
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
            Volatile.Write(ref installed, endpoints.Any(e => e.State == "Installed"));
            Log.LogInfo($"{Identifier}: {endpoints.Count(e => e.State == "Installed")}/{endpoints.Length} installed; {catalog.Excluded.Length} excluded. Observation only.");
            foreach (var status in GetStatus().Where(s => s.State is "Failed" or "Rejected")) Log.LogWarning($"{status.Id}: {status.Detail}");
            foreach (string diagnostic in FieldDiagnostics) Log.LogWarning("Field unavailable: " + diagnostic);
            int exportSeconds = Config.Bind("Diagnostics", "ExportIntervalSeconds", 0,
                "Write detached status/latest samples to BepInEx/diagnostics; 0 disables, otherwise minimum 5 seconds.").Value;
            if (exportSeconds > 0)
            {
                var exportInterval = TimeSpan.FromSeconds(Math.Clamp(exportSeconds, 5, 3600));
                diagnosticsTimer = new System.Threading.Timer(_ => ExportDiagnostics(), null, exportInterval, exportInterval);
            }
        }
        catch (Exception error) { Log.LogError(Identifier + " installation refused/failed: " + error); Unload(); }
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
        }
        return success;
    }

    private void ExportDiagnostics()
    {
        if (!Monitor.TryEnter(diagnosticsLock)) return;
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
        finally { Monitor.Exit(diagnosticsLock); }
    }

    private sealed record Capture(Endpoint Endpoint, long CallId, long InstanceAddress);
    private static void BeforeInstance(Il2CppObjectBase __instance, MethodBase __originalMethod, object[] __args, out Capture? __state)
        => Begin(__instance, __originalMethod, __args, out __state);
    private static void BeforeStatic(MethodBase __originalMethod, object[] __args, out Capture? __state)
        => Begin(null, __originalMethod, __args, out __state);
    private static void Begin(Il2CppObjectBase? instance, MethodBase method, object[] args, out Capture? state)
    {
        state = null;
        if (!Dispatch.TryGetValue(method, out var endpoint)) return;
        var owner = endpoint.Owner;
        if (!owner.Installed || endpoint.State != "Installed") return;
        Interlocked.Increment(ref endpoint.Calls);
        if (Interlocked.Read(ref endpoint.Faults) >= 3 || !endpoint.Gate.TryEnter(Stopwatch.GetTimestamp())) return;
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
        if (instance != null && (phase == "Before" || !skipAfter))
        {
            values.AddRange(snapshots!.Read(instance));
            if (CapturePlayerDetails)
            {
                values.AddRange(snapshots.ReadStatEntries(instance));
                values.AddRange(snapshots.ReadOwnedLocks(instance));
            }
        }
        values.AddRange(snapshots!.Arguments(args, endpoint.Spec, phase == "Before", CapturePlayerDetails));
        if (phase == "After" && endpoint.Spec.ReturnType != "System.Void")
        {
            values.Add(NativeSnapshots.Describe("result", result));
            if (CapturePlayerDetails && endpoint.Spec.ReturnType == "Nivalis.OverrideableBool+OverrideLock" && result is Il2CppObjectBase native)
                values.AddRange(snapshots.Read(native, "result."));
        }
        Observations.Record(callId, phase, endpoint.Spec, address, values);
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
