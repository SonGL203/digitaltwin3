namespace Warehouse.Api.Models;

public record RoomSummary(
    int Id, string Name, double Width, double Depth, double Height, double GridSize,
    int RackCount, int SlotCount, int ObstacleCount);
