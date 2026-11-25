namespace Warehouse.Api.Models;

public record AllocationProgress(
    int BatchId, int RoomId, string Status, int AfterId, int Total, int Placed, int RejectedCount, string? Error);
