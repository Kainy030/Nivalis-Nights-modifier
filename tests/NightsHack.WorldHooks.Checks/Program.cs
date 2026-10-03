using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
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
    Assert(player.Methods.Length == 323 && player.Types.Length == 32 && player.Excluded.Length == 48);
    Assert(catalogs["WorldHook"].Methods.Length == 350 && catalogs["ItemHook"].Methods.Length == 220 &&
        catalogs["GameRuntimeHook"].Methods.Length == 173 && combined.Length == 1066);
    Assert(combined.Select(m => m.Id).Distinct().Count() == combined.Length);
    Assert(combined.Select(m => Convert.ToInt64(m.Rva,16)).Distinct().Count() == combined.Length);
    Assert(all.All(m => m.Parameters.Length == m.OutParameters.Length && m.Parameters.Length == m.ParameterNames.Length));
    Assert(catalogs.Values.All(c => c.Methods.Length > 0 && c.Types.Length > 0 && c.Excluded.Length > 0));
});
Check("command runtime queues only registered targets and drains on demand", () => {
    var runtime = new CommandRuntime(1); int calls = 0;
    Assert(runtime.Register("Fixture.Target", request => { calls++; return new(request.RequestId, "Succeeded", true, "ok", "{\"value\":7}" ); }));
    var first = new CommandRequest(Guid.NewGuid(), "Fixture", "Fixture.Target", "Run", "{}");
    Assert(runtime.TryEnqueue(first));
    Assert(!runtime.TryEnqueue(new(Guid.NewGuid(), "Fixture", "Fixture.Target", "Run", "{}")));
    Assert(calls == 0 && runtime.PendingCount == 1);
    Assert(runtime.Drain() == 1 && calls == 1);
    Assert(runtime.TryGet(first.RequestId, out var response));
    Assert(response.Succeeded && response.ResultJson.Contains("7"));
});
Check("command runtime converts handler faults to uncertain side effects", () => {
    var runtime = new CommandRuntime();
    Assert(runtime.Register("Fixture.Fault", _ => throw new InvalidOperationException("boom")));
    var request = new CommandRequest(Guid.NewGuid(), "Fixture", "Fixture.Fault", "Run", "{}");
    Assert(runtime.TryEnqueue(request)); runtime.Drain();
    Assert(runtime.TryGet(request.RequestId, out var response) && response.State == "SideEffectsUnknown" && !response.Succeeded);
});
ObservationPolicyChecks.Run(Check);
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
var engineMethods=Flatten(engine.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToDictionary(m=>m.FullName);
IEnumerable<MethodReference> EngineCalls(MethodDefinition entry) {
    var pending=new Stack<MethodDefinition>(); var seen=new HashSet<string>(); pending.Push(entry);
    while(pending.Count>0) {
        var method=pending.Pop(); if(!seen.Add(method.FullName)) continue;
        foreach(var call in method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>()) {
            yield return call;
            string name=call is GenericInstanceMethod generic ? generic.ElementMethod.FullName : call.FullName;
            if(engineMethods.TryGetValue(name,out var next)) pending.Push(next);
        }
    }
}
bool StartsNativeObservation(MethodReference method) => method.Name is "EnsureNativeContext" or "ResolveTarget" or "CaptureValues" ||
    method.DeclaringType.FullName=="HarmonyLib.Harmony" || method.DeclaringType.FullName=="System.Threading.Timer" ||
    (method.Name==".ctor" && method.DeclaringType.FullName=="NightsHack.HookRuntime.NativeSnapshots");
int LocalIndex(Instruction instruction, bool store) {
    if(instruction.OpCode.Code==(store ? Code.Stloc : Code.Ldloc) || instruction.OpCode.Code==(store ? Code.Stloc_S : Code.Ldloc_S))
        return ((VariableDefinition)instruction.Operand).Index;
    Code[] codes=store ? new[]{Code.Stloc_0,Code.Stloc_1,Code.Stloc_2,Code.Stloc_3} : new[]{Code.Ldloc_0,Code.Ldloc_1,Code.Ldloc_2,Code.Ldloc_3};
    return Array.IndexOf(codes,instruction.OpCode.Code);
}
IEnumerable<Instruction> ReachableInstructions(Instruction entry) {
    var pending=new Stack<Instruction>(); var seen=new HashSet<Instruction>(); pending.Push(entry);
    while(pending.Count>0) {
        var instruction=pending.Pop(); if(!seen.Add(instruction)) continue; yield return instruction;
        if(instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) continue;
        if(instruction.Operand is Instruction target) pending.Push(target);
        if(instruction.Operand is Instruction[] targets) foreach(var item in targets) pending.Push(item);
        if(instruction.OpCode.FlowControl!=FlowControl.Branch && instruction.Next!=null) pending.Push(instruction.Next);
    }
}
Check("compiled callbacks cannot skip originals or overwrite game arguments/results", () => {
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    foreach(string name in new[] {"BeforeInstance","BeforeStatic","AfterInstanceVoid","AfterInstanceResult","AfterStaticVoid","AfterStaticResult"}) {
        var method=observer.Methods.Single(m=>m.Name==name);
        Assert(method.ReturnType.FullName=="System.Void");
        Assert(method.Parameters.All(p=>!p.ParameterType.IsByReference || p.Name=="__state"));
        if(name.Contains("Static")) Assert(method.Parameters.All(p=>p.Name!="__instance"));
    }
});
Check("compiled counter callback requests only original method and never captures values", () => {
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    var counter=observer.Methods.Single(m=>m.Name=="CounterOnly");
    Assert(counter.ReturnType.FullName=="System.Void");
    Assert(counter.Parameters.Count==1 && counter.Parameters[0].Name=="__originalMethod" &&
        counter.Parameters[0].ParameterType.FullName=="System.Reflection.MethodBase");
    Assert(!EngineCalls(counter).Any(m=>m.Name=="CaptureValues" || m.DeclaringType.FullName=="NightsHack.HookRuntime.NativeSnapshots" ||
        m.DeclaringType.FullName=="NightsHack.HookRuntime.HookObservationBuffer"));
});
Check("target resolution validates without installing patches, capturing or invoking gameplay", () => {
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    var resolver=observer.Methods.Single(m=>m.Name=="ResolveTarget");
    Assert(resolver.IsPublic && resolver.ReturnType.FullName=="System.Reflection.MethodInfo");
    var calls=EngineCalls(resolver).ToArray();
    Assert(calls.Any(m=>m.Name=="EnsureNativeContext") && calls.Any(m=>m.Name=="VerifyNativeEntry"));
    foreach(string guard in new[]{"VerifyFile","VerifyByRefMarshalling","VerifyReturnMarshalling","VerifySmallValueParameters"})
        Assert(calls.Any(m=>m.Name==guard),"Resolution lost guard: "+guard);
    Assert(!calls.Any(m=>m.Name=="CaptureValues" || m.DeclaringType.FullName=="HarmonyLib.Harmony" ||
        m.DeclaringType.FullName=="System.Threading.Timer" ||
        (m.Name==".ctor" && m.DeclaringType.FullName=="NightsHack.HookRuntime.NativeSnapshots") ||
        (m.Name=="Invoke" && m.DeclaringType.FullName.StartsWith("System.Reflection.",StringComparison.Ordinal))));
});
Check("empty selection returns before any native observation setup", () => {
    var observer=engine.MainModule.Types.Single(t=>t.Name=="ObservationPlugin");
    var load=observer.Methods.Single(m=>m.Name=="Load");
    var instructions=load.Body.Instructions;
    var select=instructions.First(i=>i.Operand is MethodReference m && m.Name=="Select" && m.DeclaringType.FullName=="NightsHack.HookRuntime.ObservationPolicy");
    int local=LocalIndex(select.Next,true);
    Assert(local>=0,"Selection must be stored before its empty check.");
    var length=instructions.First(i=>i.Offset>select.Offset && i.OpCode.Code==Code.Ldlen && LocalIndex(i.Previous,false)==local);
    var branch=length.Next; bool compareToZero=false;
    while(branch.OpCode.FlowControl!=FlowControl.Cond_Branch) {
        Assert(branch.OpCode.Code is Code.Conv_I4 or Code.Conv_I or Code.Ldc_I4_0 or Code.Ceq or Code.Nop,"Unexpected empty-selection comparison.");
        if(branch.OpCode.Code==Code.Ceq) compareToZero=true;
        branch=branch.Next;
    }
    Assert(branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S);
    bool branchesWhenTrue=branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S;
    var zeroPath=branchesWhenTrue==compareToZero ? (Instruction)branch.Operand : branch.Next;
    var reachable=ReachableInstructions(zeroPath).ToArray();
    Assert(reachable.Any(i=>i.OpCode.Code==Code.Ret),"Empty selection cannot reach a return.");
    Assert(!reachable.Select(i=>i.Operand).OfType<MethodReference>().Any(StartsNativeObservation),"Empty selection reaches native observation setup.");
    Assert(!instructions.Where(i=>i.Offset<branch.Offset).Select(i=>i.Operand).OfType<MethodReference>().Any(StartsNativeObservation),"Native observation setup precedes the empty selection guard.");
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
Check("optional Player detail capture and bounded readers remain available", () => {
    using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/NightsHack.PlayerHook/bin/Release/net6.0/PlayerHook.dll"));
    var entry=plugin.MainModule.Types.Single(t=>t.Name=="PlayerHook");
    var profile=entry.Methods.Single(m=>m.Name=="get_CapturePlayerDetails");
    Assert(profile.ReturnType.FullName=="System.Boolean");
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
Check("historical Player catalog remains intact as passive metadata", () => {
    string path=Path.Combine(root,"src/NightsHack.PlayerHook/PlayerCatalog.json");
    Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))=="40FB6DCAF34E7C3ECAD163714A44525EBC523C9B0DAAA27FE1A0E78DAEC79AAA");
});
Check("passive catalogs retain visual metadata without installing observers", () => {
    Assert(player.Methods.Any(m => m.Type == "Nivalis.PlayerCameraController" && m.Name == "SetDepthOfField"));
    Assert(player.Types.Any(t => t.Name == "Nivalis.PlayerHandsAnimator"));
    Assert(catalogs["WorldHook"].Methods.Any(m => m.Name == "EnableHighLight"));
    Assert(catalogs["ItemHook"].Methods.Any(m => m.Type == "Nivalis.PlacementSpot" && m.Name == "ToggleHighlight"));
    Assert(catalogs["GameRuntimeHook"].Methods.Any(m => m.Type == "Nivalis.DayNightCycle.LightCycleManager" && m.Name == "UpdateLighting"));
    foreach (var c in catalogs.Values.Append(player))
        Assert(ObservationPolicy.Select(c, "", false, "", "").Length == 0);
});
Check("passive catalogs retain AI metadata without selecting AI observers", () => {
    var runtime = catalogs["GameRuntimeHook"];
    var world = catalogs["WorldHook"];
    Assert(runtime.Methods.Count(m => m.Type.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)) == 17);
    Assert(world.Methods.Count(m => m.Type.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)) == 13);
    foreach (string name in new[] { "EnsureInitialized", "UpdateCurrentAgentAction", "SelectNewAction", "SwitchAction" })
        Assert(runtime.Methods.Any(m => m.Name == name && m.Type.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)));
    Assert(world.Methods.Any(m => m.Name == "UpdatePositionInternal" && m.Type.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)));
    Assert(runtime.Types.Any(t => t.Name.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)));
    Assert(world.Types.Any(t => t.Name.StartsWith("Nivalis.GhostSystem.Ai.", StringComparison.Ordinal)));
    Assert(ObservationPolicy.Select(runtime, "", false, "", "").Length == 0);
    Assert(ObservationPolicy.Select(world, "", false, "", "").Length == 0);
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
