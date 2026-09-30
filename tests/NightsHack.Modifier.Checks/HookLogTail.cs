using System.Text;
using System.Text.Json;
using NightsHack.Logging;

namespace NightsHack.Modifier;

// Incremental bounded reads in the trainer process only. Partial UTF-8 and JSON lines survive polls.
internal sealed class HookLogTail
{
    readonly string path;
    long offset;
    readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    string pending = "";
    public HookLogTail(string path) => this.path = path;
    public IEnumerable<AuditRecord> Read()
    {
        if (!File.Exists(path)) return Array.Empty<AuditRecord>();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length < offset) { offset = 0; pending = ""; decoder.Reset(); }
        file.Position = offset;
        byte[] bytes = new byte[65536]; int count = file.Read(bytes); offset += count;
        char[] chars = new char[Encoding.UTF8.GetMaxCharCount(count)];
        pending += new string(chars, 0, decoder.GetChars(bytes, 0, count, chars, 0, false));
        var lines = pending.Split('\n'); pending = lines[^1];
        if (pending.Length > 65536) throw new InvalidDataException("钩子日志记录超过读取上限。");
        var result = new List<AuditRecord>();
        foreach (string line in lines[..^1])
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(JsonSerializer.Deserialize<AuditRecord>(line) ?? throw new InvalidDataException("钩子日志记录为空。"));
        }
        return result;
    }
}
