using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NightsHack.HookRuntime;

public sealed record CommandRequest(Guid RequestId, string Feature, string TargetId, string Operation, string ArgumentsJson);
public sealed record CommandResponse(Guid RequestId, string State, bool Succeeded, string Summary, string ResultJson);

/// <summary>
/// Game-thread command boundary. Queue ownership is explicit: callers enqueue from any
/// thread, while the plugin drains only from a known game-thread callback.
/// </summary>
public sealed class CommandRuntime
{
    readonly ConcurrentDictionary<string, Func<CommandRequest, CommandResponse>> handlers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<Guid, CommandResponse> completed = new();
    readonly ConcurrentQueue<CommandRequest> pending = new();
    readonly int maxPending;
    int pendingCount;

    public CommandRuntime(int maxPending = 64)
    {
        if (maxPending <= 0) throw new ArgumentOutOfRangeException(nameof(maxPending));
        this.maxPending = maxPending;
    }

    public bool Register(string targetId, Func<CommandRequest, CommandResponse> handler)
    {
        if (string.IsNullOrWhiteSpace(targetId) || handler is null) throw new ArgumentException("Target and handler are required.");
        return handlers.TryAdd(targetId, handler);
    }

    public bool TryEnqueue(CommandRequest request)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Feature) ||
            string.IsNullOrWhiteSpace(request.TargetId) || string.IsNullOrWhiteSpace(request.Operation)) return false;
        if (!handlers.ContainsKey(request.TargetId)) return false;
        if (Interlocked.Increment(ref pendingCount) > maxPending)
        {
            Interlocked.Decrement(ref pendingCount);
            return false;
        }
        pending.Enqueue(request);
        return true;
    }

    /// <summary>Must be called by a verified game-thread callback.</summary>
    public int Drain(int maxItems = 8)
    {
        if (maxItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        int processed = 0;
        while (processed < maxItems && pending.TryDequeue(out var request))
        {
            Interlocked.Decrement(ref pendingCount);
            CommandResponse response;
            try
            {
                response = handlers.TryGetValue(request.TargetId, out var handler)
                    ? handler(request)
                    : new(request.RequestId, "Rejected", false, "Target is no longer registered.", "{}");
            }
            catch (Exception error)
            {
                response = new(request.RequestId, "SideEffectsUnknown", false, error.GetBaseException().Message, "{}");
            }
            completed[request.RequestId] = response;
            processed++;
        }
        return processed;
    }

    public bool TryGet(Guid requestId, out CommandResponse response) => completed.TryGetValue(requestId, out response!);
    public int PendingCount => Volatile.Read(ref pendingCount);
}

/// <summary>One-request-per-connection named-pipe transport. Transport threads only enqueue work.</summary>
public sealed class CommandPipeServer : IDisposable
{
    readonly string pipeName;
    readonly CommandRuntime runtime;
    readonly CancellationTokenSource stopping = new();
    Task? acceptLoop;

    public CommandPipeServer(string pipeName, CommandRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(pipeName)) throw new ArgumentException("Pipe name is required.", nameof(pipeName));
        this.pipeName = pipeName;
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Start()
    {
        if (acceptLoop is not null) throw new InvalidOperationException("Pipe server already started.");
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    async Task AcceptLoopAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096);
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), stopping.Token).ConfigureAwait(false);
                var response = ProcessLine(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            catch (TimeoutException) { }
            catch (IOException) when (stopping.IsCancellationRequested) { break; }
            catch { }
        }
    }

    CommandResponse ProcessLine(string? line)
    {
        if (line is null || line.Length > 16_384)
            return new(Guid.Empty, "Rejected", false, "Request is empty or too large.", "{}");
        CommandRequest? request;
        try { request = JsonSerializer.Deserialize<CommandRequest>(line); }
        catch (JsonException) { return new(Guid.Empty, "Rejected", false, "Invalid request JSON.", "{}"); }
        if (request is null || request.RequestId == Guid.Empty)
            return new(Guid.Empty, "Rejected", false, "Invalid request.", "{}");
        if (request.Operation.Equals("GetResult", StringComparison.Ordinal))
        {
            try
            {
                using var args = JsonDocument.Parse(request.ArgumentsJson);
                if (!args.RootElement.TryGetProperty("requestId", out var idValue) || !Guid.TryParse(idValue.GetString(), out var resultId))
                    return new(request.RequestId, "Rejected", false, "Result requestId is invalid.", "{}");
                return runtime.TryGet(resultId, out var result)
                    ? result
                    : new(request.RequestId, "Pending", true, "Command is still pending on the game thread.", "{}");
            }
            catch (JsonException) { return new(request.RequestId, "Rejected", false, "Invalid result arguments.", "{}"); }
        }
        if (!runtime.TryEnqueue(request))
        {
            return new(request.RequestId, "Rejected", false, "Target is unavailable or command queue is full.", "{}");
        }
        return new(request.RequestId, "Accepted", true, "Command queued for the game thread.", "{}");
    }

    public void Dispose()
    {
        stopping.Cancel();
        try { acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        stopping.Dispose();
    }
}

public static class CommandPipeClient
{
    public static async Task<CommandResponse> SendAsync(string pipeName, CommandRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
        string? line = await reader.ReadLineAsync().WaitAsync(linked.Token).ConfigureAwait(false);
        return line is null
            ? new(request.RequestId, "Unavailable", false, "Pipe closed before acknowledgement.", "{}")
            : JsonSerializer.Deserialize<CommandResponse>(line) ?? new(request.RequestId, "Unavailable", false, "Invalid pipe response.", "{}");
    }
}
