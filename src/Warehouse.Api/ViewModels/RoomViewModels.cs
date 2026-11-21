namespace Warehouse.Api.ViewModels;

// ViewModel = hình dạng JSON mà API trả về cho giao diện.
// Tên thuộc tính C# (FootprintWidth) tự đổi thành chữ thường đầu trong JSON (footprintWidth);
// giao diện đang đọc đúng các tên đó nên KHÔNG đổi tên trường nếu chưa sửa giao diện.

public record RackViewModel(
    int Id, string Code, double X, double Y, int Rotation,
    int Levels, double LevelHeight, int Bays, double BayWidth, double Depth,
    bool HasTop, double MaxWeight, int? ProductTypeId,
    double FootprintWidth, double FootprintDepth,   // chỗ kệ chiếm trên sàn (m), đã tính xoay
    int SlotCount);

public record ObstacleViewModel(int Id, string Kind, double X, double Y, double Width, double Depth);

public record RoomViewModel(
    int Id, string Name, double Width, double Depth, double GridSize, double MinGap, double Margin, int Version,
    IReadOnlyList<RackViewModel> Racks, IReadOnlyList<ObstacleViewModel> Obstacles);
