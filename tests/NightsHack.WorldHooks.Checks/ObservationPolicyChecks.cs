using System.Diagnostics;
using NightsHack.HookRuntime;

internal static class ObservationPolicyChecks
{
    internal static void Run(Action<string, Action> check)
    {
        var first = Method("Nivalis.PlayerState", "Set", "System.Int32");
        var overload = Method("Nivalis.PlayerState", "Set", "System.Single");
        var pair = Method("Nivalis.PlayerState", "Pair", "System.Int32", "System.Single");
        var visual = Method("Nivalis.PlayerCameraController", "UpdateCamera");
        var ai = Method("Nivalis.GhostSystem.Ai.AgentGhostSimulator", "UpdateCurrentAgentAction");
        var catalog = new Catalog(Array.Empty<TypeSpec>(), new[] { first, overload, pair, visual, ai }, Array.Empty<ExcludedSpec>());

        check("observation policy defaults to no selected methods", () =>
        {
            Assert(ObservationPolicy.Select(catalog, "", false, "", "").Length == 0);
            Assert(ObservationPolicy.Select(catalog, " \r\n ; ", false, "Trace", first.Id).Length == 0);
            Assert(ObservationPolicy.Select(catalog, "", true, "Counter", " ").Length == 0);
        });
        check("disabled diagnostics ignore stale invalid mode and allow list", () =>
        {
            var result = ObservationPolicy.Select(catalog, first.Id, false, "OldInvalidMode", "Unknown.Type.Method()");
            Assert(result.Length == 1 && result[0].Method == first && result[0].Mode == ObservationMode.Counter);
        });
        check("observation policy selects exact overloads and preserves parameter commas", () =>
        {
            var result = ObservationPolicy.Select(catalog, overload.Id + ";" + pair.Id, false, "", "");
            Assert(result.Length == 2 && result[0].Method == overload && result[1].Method == pair);
            Assert(result.All(selection => selection.Mode == ObservationMode.Counter));
        });
        check("unknown IDs wildcards groups and invalid diagnostic modes fail closed", () =>
        {
            foreach (string invalid in new[] { "Unknown.Type.Method()", "*", "Test", first.Id.ToLowerInvariant(), first.Id + "," + overload.Id })
            {
                Reject(() => ObservationPolicy.Select(catalog, invalid, false, "", ""));
                Reject(() => ObservationPolicy.Select(catalog, first.Id, true, "Counter", invalid));
            }
            foreach (string invalid in new[] { "", "Off", "counter", "3", "Counter,Trace" })
                Reject(() => ObservationPolicy.Select(catalog, first.Id, true, invalid, ""));
            Reject(() => ObservationPolicy.Select(catalog with { Methods = new[] { first, first } }, "", false, "", ""));
        });
        check("observation policy deduplicates in catalog order and diagnostics override features", () =>
        {
            var result = ObservationPolicy.Select(catalog, pair.Id + ";" + first.Id + "\r\n" + first.Id,
                true, "Trace", overload.Id + "\n" + first.Id + ";" + overload.Id);
            Assert(result.Length == 3);
            Assert(result[0].Method == first && result[0].Mode == ObservationMode.Trace);
            Assert(result[1].Method == overload && result[1].Mode == ObservationMode.Trace);
            Assert(result[2].Method == pair && result[2].Mode == ObservationMode.Counter);
        });
        check("AI and rendering catalog entries remain passive unless explicitly selected", () =>
        {
            Assert(ObservationPolicy.Select(catalog, "", false, "Trace", visual.Id + ";" + ai.Id).Length == 0);
            var result = ObservationPolicy.Select(catalog, "", true, "Counter", ai.Id + ";" + visual.Id);
            Assert(result.Length == 2 && result[0].Method == visual && result[1].Method == ai);
            Assert(result.All(selection => selection.Mode == ObservationMode.Counter));
        });
        check("observation budget requires positive finite limits", () =>
        {
            RejectArgument(() => new ObservationBudget(0, 1, 1));
            RejectArgument(() => new ObservationBudget(1, 0, 1));
            RejectArgument(() => new ObservationBudget(1, 1, 0));
            RejectArgument(() => new ObservationBudget(-1, 1, 1));
        });
        check("observation budget starts only on first capture and expires exactly at duration", () =>
        {
            long tick = Stopwatch.Frequency;
            var budget = new ObservationBudget(10, 100, 2);
            Assert(!budget.IsExpired(long.MaxValue) && !budget.IsExhausted);
            Assert(budget.TryEnter(20 * tick));
            Assert(!budget.IsExpired(22 * tick - 1));
            Assert(budget.TryEnter(22 * tick - 1));
            Assert(budget.IsExpired(22 * tick) && !budget.TryEnter(22 * tick));
            Assert(!budget.IsExhausted);
        });
        check("observation budget enforces a rolling second including exact boundary", () =>
        {
            long tick = Stopwatch.Frequency;
            var budget = new ObservationBudget(2, 100, 10);
            Assert(budget.TryEnter(0) && budget.TryEnter(tick / 2));
            Assert(!budget.TryEnter(tick - 1));
            Assert(budget.TryEnter(tick));
            Assert(!budget.TryEnter(tick));
            Assert(budget.TryEnter(tick + tick / 2));
        });
        check("rejected and out of order captures do not consume total quota", () =>
        {
            long tick = Stopwatch.Frequency;
            var budget = new ObservationBudget(1, 2, 10);
            Assert(budget.TryEnter(tick));
            Assert(!budget.TryEnter(tick) && !budget.TryEnter(tick - 1));
            Assert(!budget.IsExhausted && budget.TryEnter(2 * tick));
            Assert(budget.IsExhausted && !budget.TryEnter(3 * tick));
        });
        check("concurrent budget admissions cannot exceed per second or total limits", () =>
        {
            var perSecond = new ObservationBudget(17, 100, 10);
            int admitted = 0;
            Parallel.For(0, 4000, _ => { if (perSecond.TryEnter(0)) Interlocked.Increment(ref admitted); });
            Assert(admitted == 17);
            var total = new ObservationBudget(100, 13, 10);
            admitted = 0;
            Parallel.For(0, 4000, _ => { if (total.TryEnter(0)) Interlocked.Increment(ref admitted); });
            Assert(admitted == 13 && total.IsExhausted);
        });
    }

    private static MethodSpec Method(string type, string name, params string[] parameters) =>
        new("Test", type, name, "System.Void", parameters, parameters.Select((_, i) => "p" + i).ToArray(),
            new bool[parameters.Length], "0x1", false);

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Observation policy check failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected fail-closed selection rejection.");
    }

    private static void RejectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Expected invalid budget limit rejection.");
    }
}
