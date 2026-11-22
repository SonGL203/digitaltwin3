namespace Warehouse.Api.ViewModels;

public class CreateRoomRequest
{
    public string Name { get; set; } = "";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public double GridSize { get; set; } = 0.5;
    public double MinGap { get; set; }
    public double Margin { get; set; }
}

public record RoomCreatedViewModel(int Id);

public record RoomSummaryViewModel(
    int Id, string Name, double Width, double Depth, double Height, double GridSize,
    int RackCount, int SlotCount, int ObstacleCount);

public record RackViewModel(
    int Id, string Code, double X, double Y, int Rotation,
    int Levels, double LevelHeight, int Bays, double BayWidth, double Depth,
    bool HasTop, double MaxWeight, int? ProductTypeId,
    double FootprintWidth, double FootprintDepth,
    int SlotCount);

public record ObstacleViewModel(int Id, string Kind, double X, double Y, double Width, double Depth);

public record RoomViewModel(
    int Id, string Name, double Width, double Depth, double Height,
    double GridSize, double MinGap, double Margin, int Version,
    IReadOnlyList<RackViewModel> Racks, IReadOnlyList<ObstacleViewModel> Obstacles);
