using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NightsHack.HookRuntime;

/// <summary>Detached diagnostic value. Reference addresses are not persistent handles.</summary>
public sealed record ObservedValue(string Name, string Type, string Kind, string Value);
public sealed record HookObservation(long Sequence, long CallId, string Phase, string Group, string Method,
    DateTimeOffset Timestamp, int ThreadId, long InstanceAddress, IReadOnlyList<ObservedValue> Values);
public sealed record HookStatus(string Id, string Group, string State, string Detail, long Calls, long Samples, long Faults);

public sealed class HookObservationBuffer
{
    private readonly object sync = new();
    private readonly Queue<HookObservation> queue = new();
    private readonly Dictionary<string, HookObservation> latest = new(StringComparer.Ordinal);
    private long sequence, dropped;
    public int Capacity { get; }
    public HookObservationBuffer(int capacity = 512)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }
    public long DroppedCount { get { lock (sync) return dropped; } }
    public int Count { get { lock (sync) return queue.Count; } }
    public bool TryDequeue(out HookObservation? observation)
    {
        lock (sync) { observation = queue.Count == 0 ? null : queue.Dequeue(); return observation != null; }
    }
    public IReadOnlyList<HookObservation> GetLatest()
    { lock (sync) return Array.AsReadOnly(latest.Values.OrderBy(v => v.Sequence).ToArray()); }
    internal HookObservation Record(long callId, string phase, MethodSpec spec, long instance, IEnumerable<ObservedValue> values)
    {
        // Defensive copy: neither the game nor consumers can mutate queued snapshots.
        var detached = new ReadOnlyCollection<ObservedValue>(values.ToArray());
        lock (sync)
        {
            var observation = new HookObservation(++sequence, callId, phase, spec.Group, spec.Id,
                DateTimeOffset.UtcNow, Environment.CurrentManagedThreadId, instance, detached);
            if (queue.Count == Capacity) { queue.Dequeue(); dropped++; }
            queue.Enqueue(observation);
            // Only internal catalog IDs reach this method. At most two entries per endpoint.
            latest[spec.Id + ":" + phase] = observation;
            return observation;
        }
    }
}

internal sealed class SampleGate
{
    private readonly long interval;
    private long next;
    internal SampleGate(int milliseconds) => interval = (long)(Stopwatch.Frequency * (milliseconds / 1000.0));
    internal bool TryEnter(long now)
    {
        if (interval == 0) return true;
        while (true)
        {
            long previous = Volatile.Read(ref next);
            if (now < previous) return false;
            if (Interlocked.CompareExchange(ref next, now + interval, previous) == previous) return true;
        }
    }
}
