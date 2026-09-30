using System.Diagnostics;

namespace NightsHack.HookRuntime;

public enum ObservationMode { Off, Counter, Trace }

internal sealed record ObservationSelection(MethodSpec Method, ObservationMode Mode);

/// <summary>Only explicit, exact method IDs enable observers; a catalog is not an installation list.</summary>
internal static class ObservationPolicy
{
    internal static ObservationSelection[] Select(Catalog catalog, string featureAllowList,
        bool diagnosticsEnabled, string diagnosticMode, string diagnosticAllowList)
    {
        var known = new Dictionary<string, MethodSpec>(StringComparer.Ordinal);
        foreach (var method in catalog.Methods)
            if (!known.TryAdd(method.Id, method))
                throw new InvalidOperationException($"Duplicate catalog method ID: {method.Id}");

        var selected = new Dictionary<string, ObservationMode>(StringComparer.Ordinal);
        foreach (string id in ParseIds(featureAllowList))
        {
            RequireKnown(known, id);
            selected[id] = ObservationMode.Counter;
        }

        // Stale diagnostic settings must neither install hooks nor prevent normal startup.
        if (diagnosticsEnabled)
        {
            ObservationMode mode = diagnosticMode?.Trim() switch
            {
                "Counter" => ObservationMode.Counter,
                "Trace" => ObservationMode.Trace,
                _ => throw new InvalidOperationException("Enabled diagnostics require mode Counter or Trace.")
            };
            foreach (string id in ParseIds(diagnosticAllowList))
            {
                RequireKnown(known, id);
                selected[id] = mode;
            }
        }

        // Deduplicate selections and retain catalog ordering regardless of list ordering.
        return catalog.Methods.Where(method => selected.ContainsKey(method.Id))
            .Select(method => new ObservationSelection(method, selected[method.Id])).ToArray();
    }

    private static IEnumerable<string> ParseIds(string list) => string.IsNullOrWhiteSpace(list)
        ? Array.Empty<string>()
        : list.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void RequireKnown(Dictionary<string, MethodSpec> known, string id)
    {
        if (!known.ContainsKey(id))
            throw new InvalidOperationException($"Unknown observation method ID: {id}. Use an exact catalog signature; wildcards and groups are not supported.");
    }
}

/// <summary>Shared admission budget for expensive captures; reserve before reading native fields or allocating snapshots.</summary>
internal sealed class ObservationBudget
{
    private readonly object sync = new();
    private readonly Queue<long> admitted = new();
    private readonly int maxPerSecond, maxTotal;
    private readonly long durationTicks;
    private bool started;
    private long startedAt, lastTimestamp;
    private int total;

    internal ObservationBudget(int maxPerSecond, int maxTotal, int durationSeconds)
    {
        if (maxPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerSecond));
        if (maxTotal <= 0) throw new ArgumentOutOfRangeException(nameof(maxTotal));
        if (durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        this.maxPerSecond = maxPerSecond;
        this.maxTotal = maxTotal;
        durationTicks = checked((long)durationSeconds * Stopwatch.Frequency);
    }

    internal bool IsExhausted { get { lock (sync) return total >= maxTotal; } }

    internal bool IsExpired(long timestamp)
    {
        lock (sync) return started && ElapsedAtLeast(timestamp, startedAt, durationTicks);
    }

    internal bool TryEnter(long timestamp)
    {
        lock (sync)
        {
            if (total >= maxTotal) return false;
            if (!started)
            {
                started = true;
                startedAt = lastTimestamp = timestamp;
            }
            else
            {
                // Concurrent callers may acquire timestamps before entering this lock.
                // Reject an older timestamp instead of opening an earlier window or spending quota.
                if (timestamp < lastTimestamp) return false;
                lastTimestamp = timestamp;
                if (ElapsedAtLeast(timestamp, startedAt, durationTicks)) return false;
            }

            // A rolling window prevents the double burst possible at fixed second boundaries.
            while (admitted.Count > 0 && ElapsedAtLeast(timestamp, admitted.Peek(), Stopwatch.Frequency))
                admitted.Dequeue();
            if (admitted.Count >= maxPerSecond) return false;
            admitted.Enqueue(timestamp);
            total++;
            return true;
        }
    }

    private static bool ElapsedAtLeast(long timestamp, long previous, long interval) =>
        timestamp >= previous && unchecked((ulong)(timestamp - previous)) >= (ulong)interval;
}
