using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NightsHack.HookRuntime;

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
void Reject(Action action) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); }
MethodSpec Spec(string name, string result, params string[] args) => new("Test", typeof(Fixture).FullName!, name, result,
    args, args.Select((_,i) => "p" + i).ToArray(), args.Select(_ => false).ToArray(), "0x1", false);

var catalog = Catalog.Load(Assembly.GetExecutingAssembly(), "PlayerHook.Catalog.json");
Check("catalog identity, unique signatures and native RVAs", () => {
    Assert(catalog.Methods.Length == 324 && catalog.Types.Length == 32 && catalog.Excluded.Length == 46);
    Assert(catalog.Methods.Select(m => m.Id).Distinct().Count() == catalog.Methods.Length);
    Assert(catalog.Methods.Select(m => m.Rva).Distinct().Count() == catalog.Methods.Length);
    Assert(catalog.Methods.All(m => m.Parameters.Length == m.OutParameters.Length && m.Parameters.Length == m.ParameterNames.Length));
});
Check("core subsystems represented", () => {
    foreach (var group in new[] { "Lifecycle", "Player", "Stats", "Character", "Movement", "Interaction", "Focus", "Holding", "Placement",
        "Animation", "Camera", "Environment", "Navigation", "Inventory", "Skills", "Ghost", "Save", "Input" })
        Assert(catalog.Methods.Any(m => m.Group == group));
    foreach (var target in new[] { "SetStat", "set_Money", "TeleportPlayer", "AddExperience", "SaveInternal", "MakePreparationStep", "UpdateRank" })
        Assert(catalog.Methods.Any(m => m.Name == target));
});
Check("shared CancelOrder and travel native entry excluded", () => {
    Assert(!catalog.Methods.Any(m => m.Type == "Nivalis.PlayerManager+Player" && (m.Name == "CancelOrder" || m.Name == "PrePlayerTravel")));
    Assert(catalog.Excluded.Any(m => m.Name == "CancelOrder" && m.Reason.Contains("Shared")));
});
Check("exact overload selected", () => {
    var method = TargetValidation.Resolve(typeof(Fixture), Spec("Set", "System.Void", "System.Int32"));
    Assert(method.GetParameters()[0].ParameterType == typeof(int));
});
Check("wrong return/parameter/type/static/generic rejected", () => {
    Reject(() => TargetValidation.Resolve(typeof(Fixture), Spec("Set", "System.Int32", "System.Int32")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture), Spec("Set", "System.Void", "System.Double")));
    Reject(() => TargetValidation.Resolve(typeof(string), Spec("Set", "System.Void", "System.Int32")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture), Spec("Static", "System.Void")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture), Spec("Generic", "System.Void")));
});
Check("ref and out distinguished", () => {
    var spec = Spec("Out", "System.Void", "System.Int32&");
    Reject(() => TargetValidation.Resolve(typeof(Fixture), spec));
    Assert(TargetValidation.Resolve(typeof(Fixture), spec with { OutParameters = new[] { true } }).Name == "Out");
});
Check("nested generic and array type canonicalization", () => {
    Assert(TargetValidation.Canonical(typeof(Dictionary<string,List<int[]>>)) == "System.Collections.Generic.Dictionary`2<System.String,System.Collections.Generic.List`1<System.Int32[]>>");
    Assert(TargetValidation.Canonical(typeof(int[,])) == "System.Int32[,]");
});
Check("hash validation rejects changed content", () => {
    string path = Path.GetTempFileName();
    try {
        File.WriteAllText(path, "baseline");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        TargetValidation.VerifyFile(path, hash);
        File.WriteAllText(path, "changed");
        Reject(() => TargetValidation.VerifyFile(path, hash));
    } finally { File.Delete(path); }
});
var recordSpec = Spec("Set", "System.Void", "System.Int32");
Check("queue bounds and drop accounting", () => {
    var buffer = new HookObservationBuffer(2);
    for (int i=0;i<5;i++) buffer.Record(i, "Before", recordSpec, 42, Array.Empty<ObservedValue>());
    Assert(buffer.Count == 2 && buffer.DroppedCount == 3);
    Assert(buffer.TryDequeue(out var first) && first!.CallId == 3);
    Assert(buffer.TryDequeue(out var second) && second!.CallId == 4);
    Assert(!buffer.TryDequeue(out _));
});
Check("snapshots detached and phase latest kept separately", () => {
    var values = new[] { new ObservedValue("money", "System.Int32", "scalar", "1") };
    var buffer = new HookObservationBuffer();
    buffer.Record(1, "Before", recordSpec, 42, values);
    values[0] = values[0] with { Value = "999" };
    buffer.Record(1, "After", recordSpec, 42, values);
    var latest = buffer.GetLatest();
    Assert(latest.Count == 2 && latest[0].Values[0].Value == "1" && latest[1].Values[0].Value == "999");
    Assert(latest[0].CallId == latest[1].CallId);
});
Check("concurrent records retain unique ordered sequence", () => {
    var buffer = new HookObservationBuffer(128);
    Parallel.For(0, 4000, i => buffer.Record(i, "Before", recordSpec, i, Array.Empty<ObservedValue>()));
    Assert(buffer.Count == 128 && buffer.DroppedCount == 3872);
    long last = 0;
    while (buffer.TryDequeue(out var item)) { Assert(item!.Sequence > last); last = item.Sequence; }
    Assert(last == 4000);
});
Check("sample gate throttles frequent calls and admits next interval", () => {
    var gate = new SampleGate(250);
    long now = Stopwatch.Frequency;
    Assert(gate.TryEnter(now)); Assert(!gate.TryEnter(now)); Assert(!gate.TryEnter(now+1));
    Assert(gate.TryEnter(now + Stopwatch.Frequency));
});
Check("unsampled events never drop from concurrent timestamp ordering", () => {
    var gate = new SampleGate(0);
    Assert(gate.TryEnter(100)); Assert(gate.TryEnter(99)); Assert(gate.TryEnter(100));
});

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/NightsHack.PlayerHook/bin/Release/net6.0/PlayerHook.dll"));
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
using var runtime = AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/NightsHack.HookRuntime/bin/Release/net6.0/NightsHack.HookRuntime.dll"));
var allTypes = Flatten(plugin.MainModule.Types).Concat(Flatten(runtime.MainModule.Types)).ToArray();
Check("compiled callbacks cannot skip original or assign arguments/results", () => {
    var type = allTypes.Single(t => t.FullName == "NightsHack.HookRuntime.ObservationPlugin");
    foreach (var name in new[] { "BeforeInstance", "AfterInstanceVoid", "AfterInstanceResult" }) {
        var method = type.Methods.Single(m => m.Name == name);
        Assert(method.ReturnType.FullName == "System.Void");
        Assert(method.Parameters.All(p => !p.ParameterType.IsByReference || p.Name == "__state"));
    }
});
Check("compiled observer has no IL2CPP write/invoke or game method references", () => {
    var calls = allTypes.SelectMany(t => t.Methods).Where(m => m.HasBody)
        .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToArray();
    Assert(!calls.Any(m => m.Name.Contains("field_set") || m.Name.Contains("runtime_invoke") || m.Name.Contains("WriteProcessMemory")));
    Assert(!plugin.MainModule.AssemblyReferences.Any(a => a.Name == "Assembly-CSharp"));
    // This is a static guard, not proof of native interception or of every reflection call's behavior.
});
Console.WriteLine($"{passed} managed/static checks passed. No game/native hook integration was executed.");

public sealed class Fixture
{
    public void Set(int value) { }
    public void Set(float value) { }
    public static void Static() { }
    public void Generic<T>() { }
    public void Out(out int value) => value = 7;
}
