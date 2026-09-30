namespace NightsHack.ItemHook;

/// <summary>Stable definition GUID, not a localized name, world entity ID or memory address.</summary>
public sealed record AddBackpackItemRequest(Guid RequestId, string ItemTypeGuid, int Quantity);

public enum BackpackItemAdditionStatus
{
    NotImplemented,
    InvalidRequest,
    Cancelled,
    Unavailable,
    Failed,
    Partial,
    Succeeded
}

/// <summary>AddedQuantity reports actual accepted items, never a guessed success count.</summary>
public sealed record AddBackpackItemResult(Guid RequestId, BackpackItemAdditionStatus Status,
    int RequestedQuantity, int AddedQuantity, string Detail);

/// <summary>
/// Contract for a future in-process command service targeting the current local player's backpack.
/// Future implementations must resolve live state and dispatch to the game thread. No native pointers cross this boundary.
/// </summary>
public interface IBackpackItemAddition
{
    bool IsImplemented { get; }
    Task<AddBackpackItemResult> AddToBackpackAsync(AddBackpackItemRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Non-mutating reservation. It never queues a request or touches the game.</summary>
internal sealed class ReservedBackpackItemAddition : IBackpackItemAddition
{
    public bool IsImplemented => false;
    public Task<AddBackpackItemResult> AddToBackpackAsync(AddBackpackItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BackpackItemAdditionStatus status;
        string detail;
        if (cancellationToken.IsCancellationRequested)
        { status = BackpackItemAdditionStatus.Cancelled; detail = "Cancelled before dispatch; no request was queued."; }
        else if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.ItemTypeGuid) || request.Quantity <= 0)
        { status = BackpackItemAdditionStatus.InvalidRequest; detail = "RequestId, ItemTypeGuid and a positive quantity are required."; }
        else
        { status = BackpackItemAdditionStatus.NotImplemented; detail = "Reserved interface only. Live inventory resolution, game-thread dispatch and item creation are not implemented; no items added."; }
        return Task.FromResult(new AddBackpackItemResult(request.RequestId, status, request.Quantity, 0, detail));
    }
}
