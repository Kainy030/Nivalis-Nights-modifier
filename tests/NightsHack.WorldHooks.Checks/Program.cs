using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using NightsHack.HookRuntime;
using NightsHack.ItemHook;

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Reject(Action action) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); }
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var names = new[] { "WorldHook", "ItemHook", "GameRuntimeHook" };
var catalogs = names.ToDictionary(n => n, n => Catalog.Load(Assembly.GetExecutingAssembly(), n + ".Catalog.json"));
var all = catalogs.Values.SelectMany(c => c.Methods).ToArray();
var player = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(root, "src/NightsHack.PlayerHook/PlayerCatalog.json")))!;
using var metadata = AssemblyDefinition.ReadAssembly(Path.Combine(root, "work/il2cpp-validation/DummyDll/Assembly-CSharp.dll"));
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
var gameTypes = Flatten(metadata.MainModule.Types).ToDictionary(t => t.FullName.Replace('/', '+'));
using var mapping = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "work/il2cpp-validation/script.json")));
var aliases = mapping.RootElement.GetProperty("ScriptMethod").EnumerateArray().GroupBy(m => m.GetProperty("Address").GetInt64())
    .ToDictionary(g => g.Key, g => g.Select(m => m.GetProperty("Name").GetString()).Distinct().ToArray());

Check("four catalogs have disjoint signatures and native entries", () => {
    var combined = all.Concat(player.Methods).ToArray();
    Assert(combined.Select(m => m.Id).Distinct().Count() == combined.Length);
    Assert(combined.Select(m => Convert.ToInt64(m.Rva,16)).Distinct().Count() == combined.Length);
    Assert(all.All(m => m.Parameters.Length == m.OutParameters.Length && m.Parameters.Length == m.ParameterNames.Length));
    Assert(catalogs.Values.All(c => c.Methods.Length > 0 && c.Types.Length > 0 && c.Excluded.Length > 0));
});
Check("every candidate matches original metadata signature, flags and RVA", () => {
    foreach (var spec in all) {
        var matches = gameTypes[spec.Type].Methods.Where(m => m.Name == spec.Name && m.IsStatic == spec.IsStatic &&
            m.ReturnType.FullName.Replace('/','+') == spec.ReturnType &&
            m.Parameters.Select(p => p.ParameterType.FullName.Replace('/','+')).SequenceEqual(spec.Parameters) &&
            m.Parameters.Select(p => p.IsOut).SequenceEqual(spec.OutParameters)).ToArray();
        Assert(matches.Length == 1, spec.Id);
        var method = matches[0];
        Assert(!method.HasGenericParameters && !method.IsAbstract && !method.IsConstructor);
        string rva = (string)method.CustomAttributes.Single(a => a.AttributeType.Name == "AddressAttribute").Fields.Single(f => f.Name == "RVA").Argument.Value;
        Assert(Convert.ToInt64(rva,16) == Convert.ToInt64(spec.Rva,16), spec.Id);
        Assert(aliases[Convert.ToInt64(spec.Rva,16)].Length == 1, "Shared entry: " + spec.Id);
    }
});
Check("field schemas match metadata and contain no static fields", () => {
    foreach (var catalog in catalogs.Values)
        foreach (var schema in catalog.Types)
            foreach (var field in schema.Fields) {
                var actual = gameTypes[schema.Name].Fields.Single(f => f.Name == field.Name);
                Assert(!actual.IsStatic && actual.FieldType.FullName.Replace('/','+') == field.Type);
            }
});
Check("player item entry points remain in PlayerHook only", () => {
    foreach (string name in new[] { "StoreEntity", "AddItem", "AddAllFurniture", "ClearItems" })
        Assert(catalogs["ItemHook"].Excluded.Any(e => e.Name == name && e.Reason.Contains("PlayerHook")));
    Assert(!all.Any(m => m.Type is "Nivalis.PlayerObjectHolder" or "Nivalis.InventorySystem.PlayerInventory"));
});
Check("core ownership and inventory overloads", () => {
    Assert(catalogs["WorldHook"].Methods.Any(m => m.Name == "TransferGhostToScene"));
    Assert(catalogs["WorldHook"].Methods.Any(m => m.Name == "RegisterGhost"));
    var item = catalogs["ItemHook"].Methods;
    Assert(item.Count(m => m.Type == "Nivalis.InventorySystem.ItemContainer" && m.Name == "TryAdd") == 5);
    Assert(item.Any(m => m.Name == "TryCreateInInventory" && !m.Sampled));
    Assert(item.Any(m => m.Name == "UpdateDecay" && !m.Sampled));
    Assert(item.Any(m => m.Name == "SafeCreate" && m.IsStatic));
    Assert(catalogs["GameRuntimeHook"].Methods.Any(m => m.Type == "Nivalis.SerializationManager" && m.Name == "Save"));
    Assert(catalogs["GameRuntimeHook"].Methods.Any(m => m.Name == "AddTime"));
});
Check("seven actual iterator execution methods included", () => {
    string[] types = { "Nivalis.GameSceneManager+<LoadAreaRoutine>d__85", "Nivalis.GameSceneManager+<WaitForLevelLoadingUnblocked>d__86",
        "Nivalis.TransitionManager+<TransitionRoutine>d__23", "Nivalis.TravelManager+<TeleportPlayer>d__15",
        "Nivalis.HoldableEntity+<PlaceRoutine>d__94", "Nivalis.SerializationManager+<LoadRoutine>d__42",
        "Nivalis.SerializationManager+<UpdateSceneObjects>d__19" };
    foreach (string type in types) Assert(all.Any(m => m.Type == type && m.Name == "MoveNext" && m.SkipAfterInstance));
});
Check("empty GhostManagerSave native body is never patched", () => {
    Assert(!all.Any(m => Convert.ToInt64(m.Rva,16) == 0x4E8210));
    Assert(catalogs["WorldHook"].Excluded.Any(e => e.Type.EndsWith("GhostManagerSave") && e.Name == "Save"));
});
Check("every previously disassembled method has a candidate or explicit exclusion", () => {
    using var disassembly=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"work/world-investigation/disassembly-index.json")));
    foreach(var row in disassembly.RootElement.EnumerateArray()) {
        var method=row.GetProperty("method"); string symbol=method.GetProperty("Name").GetString()!;
        string[] parts=symbol.Split("$$"); long rva=method.GetProperty("Address").GetInt64();
        bool matched=all.Any(m=>m.Type.Replace('+','.')==parts[0] && m.Name==parts[1] && Convert.ToInt64(m.Rva,16)==rva) ||
            catalogs.Values.SelectMany(c=>c.Excluded).Any(e=>e.Type.Replace('+','.')==parts[0] && e.Name==parts[1] && Convert.ToInt64(e.Rva,16)==rva);
        Assert(matched,"Unaccounted investigated method: "+symbol);
    }
});

MethodSpec Spec(string name, string result, bool isStatic = false, params string[] args) => new("Test", typeof(Fixture).FullName!, name, result,
    args, args.Select((_,i) => "p"+i).ToArray(), args.Select(_ => false).ToArray(), "0x1", false, isStatic);
Check("resolver distinguishes overloads, instance/static, return and out/ref", () => {
    Assert(TargetValidation.Resolve(typeof(Fixture),Spec("Set","System.Void",false,"System.Int32")).GetParameters()[0].ParameterType == typeof(int));
    Assert(TargetValidation.Resolve(typeof(Fixture),Spec("Static","System.Void",true)).IsStatic);
    Reject(() => TargetValidation.Resolve(typeof(Fixture),Spec("Static","System.Void")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture),Spec("Set","System.Int32",false,"System.Int32")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture),Spec("Out","System.Void",false,"System.Int32&")));
    Assert(TargetValidation.Resolve(typeof(Fixture),Spec("Out","System.Void",false,"System.Int32&") with { OutParameters = new[] {true} }).Name == "Out");
    Reject(() => TargetValidation.Resolve(typeof(string),Spec("Set","System.Void",false,"System.Int32")));
    Reject(() => TargetValidation.Resolve(typeof(Fixture),Spec("Generic","System.Void")));
});
Check("canonical nested generics and arrays", () => {
    Assert(TargetValidation.Canonical(typeof(Dictionary<string,List<int[]>>)) == "System.Collections.Generic.Dictionary`2<System.String,System.Collections.Generic.List`1<System.Int32[]>>");
    Assert(TargetValidation.Canonical(typeof(int[,])) == "System.Int32[,]");
});
Check("input identity change refuses installation", () => {
    string path = Path.GetTempFileName();
    try {
        File.WriteAllText(path,"baseline"); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        TargetValidation.VerifyFile(path,hash); File.WriteAllText(path,"changed"); Reject(() => TargetValidation.VerifyFile(path,hash));
    } finally { File.Delete(path); }
});
var recordSpec = Spec("Set","System.Void",false,"System.Int32");
Check("bounded detached observations preserve call correlation and phase", () => {
    var buffer = new HookObservationBuffer(2);
    var values = new[] {new ObservedValue("n","int","scalar","1")};
    buffer.Record(9,"Before",recordSpec,0,values); values[0] = values[0] with { Value="2" };
    buffer.Record(9,"After",recordSpec,0,values);
    var latest=buffer.GetLatest(); Assert(latest.Count==2 && latest[0].Values[0].Value=="1" && latest[1].CallId==9);
    buffer.Record(10,"Before",recordSpec,0,values); Assert(buffer.Count==2 && buffer.DroppedCount==1);
});
Check("concurrent observations remain bounded and ordered", () => {
    var buffer = new HookObservationBuffer(128);
    Parallel.For(0,4000,i=>buffer.Record(i,"Before",recordSpec,i,Array.Empty<ObservedValue>()));
    Assert(buffer.Count==128 && buffer.DroppedCount==3872); long last=0;
    while(buffer.TryDequeue(out var o)) { Assert(o!.Sequence>last);last=o.Sequence; } Assert(last==4000);
});
Check("only sampled events are throttled", () => {
    var gate=new SampleGate(250); long now=Stopwatch.Frequency;
    Assert(gate.TryEnter(now) && !gate.TryEnter(now) && gate.TryEnter(now+Stopwatch.Frequency));
    gate=new SampleGate(0); Assert(gate.TryEnter(100) && gate.TryEnter(99));
});
IBackpackItemAddition service=new ReservedBackpackItemAddition();
var request=new AddBackpackItemRequest(Guid.NewGuid(),"definition-guid",3);
Check("reserved backpack API never claims or performs a successful addition", () => {
    Assert(!service.IsImplemented);
    var result=service.AddToBackpackAsync(request).GetAwaiter().GetResult();
    Assert(result.Status==BackpackItemAdditionStatus.NotImplemented && result.AddedQuantity==0 && result.RequestedQuantity==3 && result.RequestId==request.RequestId);
    var repeated=service.AddToBackpackAsync(request).GetAwaiter().GetResult();
    Assert(repeated==result);
});
Check("backpack API rejects invalid input and observes pre-dispatch cancellation", () => {
    foreach(var invalid in new[] {request with {Quantity=0},request with {Quantity=-5},request with {ItemTypeGuid=" "},request with {RequestId=Guid.Empty}}) {
        var result=service.AddToBackpackAsync(invalid).Result;
        Assert(result.Status==BackpackItemAdditionStatus.InvalidRequest && result.AddedQuantity==0);
    }
    var cancelled=service.AddToBackpackAsync(request,new CancellationToken(true)).Result;
    Assert(cancelled.Status==BackpackItemAdditionStatus.Cancelled && cancelled.AddedQuantity==0);
});

using var engine=AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/NightsHack.HookRuntime/bin/Release/net6.0/NightsHack.HookRuntime.dll"));
Check("compiled callbacks cannot skip originals or overwrite game arguments/results", () => {
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    foreach(string name in new[] {"BeforeInstance","BeforeStatic","AfterInstanceVoid","AfterInstanceResult","AfterStaticVoid","AfterStaticResult"}) {
        var method=observer.Methods.Single(m=>m.Name==name);
        Assert(method.ReturnType.FullName=="System.Void");
        Assert(method.Parameters.All(p=>!p.ParameterType.IsByReference || p.Name=="__state"));
        if(name.Contains("Static")) Assert(method.Parameters.All(p=>p.Name!="__instance"));
    }
});
Check("compiled engine and plugins contain no game writes/invocation or DummyDll reference", () => {
    var paths=names.Append("PlayerHook").Select(n=>Path.Combine(root,$"src/NightsHack.{n}/bin/Release/net6.0/{n}.dll"))
        .Append(Path.Combine(root,"src/NightsHack.HookRuntime/bin/Release/net6.0/NightsHack.HookRuntime.dll"));
    foreach(string path in paths) {
        using var binary=AssemblyDefinition.ReadAssembly(path);
        Assert(!binary.MainModule.AssemblyReferences.Any(a=>a.Name=="Assembly-CSharp"));
        var calls=Flatten(binary.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody)
            .SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        Assert(!calls.Any(m=>m.Name.Contains("field_set") || m.Name.Contains("runtime_invoke") || m.Name.Contains("WriteProcessMemory")));
        foreach (var method in Flatten(binary.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody))
        {
            bool reflects = method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>()
                .Any(m=>m.Name=="Invoke" && m.DeclaringType.FullName.StartsWith("System.Reflection."));
            if (reflects) Assert(method.DeclaringType.FullName=="NightsHack.HookRuntime.NativeSnapshots" && method.Name=="ReadStatEntries",
                "Reflection invocation outside the existing bounded Player dictionary reader.");
        }
    }
});
Check("compiled plugins embed exactly the reviewed catalogs", () => {
    foreach(string name in names.Append("PlayerHook")) {
        using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,$"src/NightsHack.{name}/bin/Release/net6.0/{name}.dll"));
        var resource=plugin.MainModule.Resources.OfType<EmbeddedResource>().Single(r=>r.Name==name+".Catalog.json");
        string file = name=="PlayerHook" ? "PlayerCatalog.json" : "Catalog.json";
        Assert(resource.GetResourceData().SequenceEqual(File.ReadAllBytes(Path.Combine(root,$"src/NightsHack.{name}/{file}"))));
        var entry=plugin.MainModule.Types.Single(t=>t.Name==name);
        Assert(entry.BaseType.FullName=="NightsHack.HookRuntime.ObservationPlugin");
        Assert(entry.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"));
        Assert(plugin.MainModule.AssemblyReferences.Any(a=>a.Name=="NightsHack.HookRuntime"));
        Assert(!Flatten(plugin.MainModule.Types).Any(t=>t.Name is "NativeSnapshots" or "Catalog" or "TargetValidation" or "SampleGate"));
    }
});
Check("Player detail profile and bounded readers survive runtime migration", () => {
    using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/NightsHack.PlayerHook/bin/Release/net6.0/PlayerHook.dll"));
    var entry=plugin.MainModule.Types.Single(t=>t.Name=="PlayerHook");
    var profile=entry.Methods.Single(m=>m.Name=="get_CapturePlayerDetails");
    Assert(profile.Body.Instructions.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    var capture=observer.Methods.Single(m=>m.Name=="CaptureValues");
    var calls=capture.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
    Assert(calls.Any(m=>m.Name=="ReadStatEntries") && calls.Any(m=>m.Name=="ReadOwnedLocks") && calls.Any(m=>m.Name=="get_CapturePlayerDetails"));
    var snapshots=engine.MainModule.Types.Single(t=>t.Name=="NativeSnapshots");
    var reader=snapshots.Methods.Single(m=>m.Name=="ReadStatEntries");
    Assert(reader.Body.Instructions.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_S && Convert.ToInt32(i.Operand)==32));
    foreach(string text in new[]{"GetEnumerator","MoveNext","Dispose","_statValues","PerSkillExperience"})
        Assert(reader.Body.Instructions.Any(i=>i.Operand as string==text));
    Assert(snapshots.Methods.Single(m=>m.Name=="ReadField").Body.Instructions.Any(i=>i.Operand as string=="Nivalis.PlayerManager+PlayerKnowledge"));
    Assert(snapshots.Methods.Single(m=>m.Name=="Arguments").Body.Instructions.Any(i=>i.Operand as string=="Nivalis.PlayerStat"));
});
Check("Player catalog remains byte-identical through the refactor", () => {
    string path=Path.Combine(root,"src/NightsHack.PlayerHook/PlayerCatalog.json");
    Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))=="40FB6DCAF34E7C3ECAD163714A44525EBC523C9B0DAAA27FE1A0E78DAEC79AAA");
});
Check("byref projected native structs rejected before unsafe wrapper invocation", () => {
    foreach (string name in new[] { "OutProjected", "RefProjected" })
        Reject(() => TargetValidation.VerifyByRefMarshalling(typeof(Fixture).GetMethod(name)!));
});
Check("all ref/out signatures quarantined including float trampoline failures", () => {
    foreach (string name in new[] { "Out", "OutReference", "RefFloat", "OutFloat" })
        Reject(() => TargetValidation.VerifyByRefMarshalling(typeof(Fixture).GetMethod(name)!));
    TargetValidation.VerifyByRefMarshalling(typeof(Fixture).GetMethod("ValueProjected")!);
});
Check("projected struct returns quarantined while managed values and references remain eligible", () => {
    Reject(() => TargetValidation.VerifyReturnMarshalling(typeof(Fixture).GetMethod("ReturnProjected")!, t => t == typeof(ProjectedStruct)));
    TargetValidation.VerifyReturnMarshalling(typeof(Fixture).GetMethod("ReturnReference")!, _ => false);
    TargetValidation.VerifyReturnMarshalling(typeof(Fixture).GetMethod("ReturnScalar")!, _ => true);
});
Check("inline projected structs rejected without excluding large indirect structs", () => {
    var method = typeof(Fixture).GetMethod("ValueProjected")!;
    foreach (int size in new[] {1,2,4,8}) Reject(() => TargetValidation.VerifySmallValueParameters(method, _ => size));
    TargetValidation.VerifySmallValueParameters(method, _ => 16);
    TargetValidation.VerifySmallValueParameters(method, _ => null);
});
Check("reproduced HoldableEntity Update incompatibility is narrowly quarantined", () => {
    var spec = new MethodSpec("HoldableEntity", "Nivalis.HoldableEntity", "Update", "System.Void",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<bool>(), "0x840710", true);
    Reject(() => TargetValidation.VerifyKnownNativeCompatibility(spec));
    TargetValidation.VerifyKnownNativeCompatibility(spec with { Name = "Start" });
    TargetValidation.VerifyKnownNativeCompatibility(spec with { Type = "Nivalis.Other" });
    TargetValidation.VerifyKnownNativeCompatibility(spec with { Rva = "0x840720" });
});
Console.WriteLine($"{passed} managed/static checks passed. No loader/native/game integration executed.");

public sealed class Fixture
{
    public void Set(int value) { }
    public void Set(float value) { }
    public static void Static() { }
    public void Generic<T>() { }
    public void Out(out int value) => value=7;
    public void RefFloat(ref float value) { }
    public void OutFloat(out float value) => value=0;
    public void OutProjected(out ProjectedStruct value) => value = new();
    public void RefProjected(ref ProjectedStruct value) { }
    public void ValueProjected(ProjectedStruct value) { }
    public void OutReference(out object value) => value = new();
    public ProjectedStruct ReturnProjected() => new();
    public object ReturnReference() => new();
    public int ReturnScalar() => 0;
}
public sealed class ProjectedStruct { }
