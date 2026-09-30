using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NightsHack.Logging;

public sealed record AuditRecord
{
    public int Schema { get; init; } = 1;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Sequence { get; init; }
    public int ProcessId { get; init; } = Environment.ProcessId;
    public string Session { get; init; } = "";
    public string Level { get; init; } = "INFO";
    public string Code { get; init; } = "";
    public string Component { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string Feature { get; init; } = "";
    public string Hook { get; init; } = "";
    public string Target { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Message { get; init; } = "";
    public string ToDisplay()
    {
        var fields = new List<string> { $"PID={ProcessId}" };
        foreach (var pair in new[] { ("Request", RequestId), ("Hook", Hook), ("Feature", Feature), ("Target", Target), ("Operation", Operation), ("Outcome", Outcome) })
            if (!string.IsNullOrEmpty(pair.Item2)) fields.Add(pair.Item1 + "=" + pair.Item2);
        return $"{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{Code}] {Message} | {string.Join(" ", fields)}";
    }
}

public static class LogSession
{
    public static string Key(int pid, DateTime start) => $"{pid}-{start.ToUniversalTime().Ticks}";
    public static string Current { get; } = CurrentKey();
    static string CurrentKey() { using var p = Process.GetCurrentProcess(); return Key(p.Id, p.StartTime); }
}

/// <summary>Event-driven, immediately flushed JSONL. No timer, sampler, or game-method observer.</summary>
public sealed class EventJournal : IDisposable
{
    readonly object gate = new();
    readonly StreamWriter writer;
    long sequence;
    public string Path { get; }
    public EventJournal(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        writer = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
    }
    public AuditRecord Write(AuditRecord entry)
    {
        lock (gate)
        {
            var record = entry with { Sequence = ++sequence, Session = LogSession.Current,
                Message = entry.Message.Length > 8192 ? entry.Message[..8192] + " [truncated]" : entry.Message };
            writer.WriteLine(JsonSerializer.Serialize(record));
            return record;
        }
    }
    public void Dispose() { lock (gate) writer.Dispose(); }
}
