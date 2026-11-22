namespace Warehouse.Api.ViewModels;

<<<<<<< HEAD
using System.ComponentModel.DataAnnotations;
using Warehouse.Api.Models;

// Dữ liệu vào/ra của các API phòng kho. Tên trường trả về dạng camelCase (id, name, levelHeight…),
// đúng như frontend đang đọc. Đơn vị: mét, kg.

// ---------- Vào ----------

/// <summary>POST /api/rooms: tạo phòng trống (chưa có kệ, tường, cột).</summary>
public class CreateRoomRequest
{
    [Required(ErrorMessage = "Chưa nhập tên kho.")]
    [StringLength(50, ErrorMessage = "Tên kho tối đa 50 ký tự.")]
    public string Name { get; set; } = "";

    [Range(0.5, 500, ErrorMessage = "Chiều dài phải từ 0,5 đến 500 m.")]
    public double Width { get; set; }

    [Range(0.5, 500, ErrorMessage = "Chiều rộng phải từ 0,5 đến 500 m.")]
    public double Depth { get; set; }

    [Range(1, 50, ErrorMessage = "Chiều cao trần phải từ 1 đến 50 m.")]
    public double Height { get; set; }

    [Range(0.05, 10, ErrorMessage = "Bước lưới phải từ 0,05 đến 10 m.")]
    public double GridSize { get; set; } = 0.5;

    [Range(0, 20, ErrorMessage = "Lối đi tối thiểu phải từ 0 đến 20 m.")]
    public double MinGap { get; set; }

    [Range(0, 2, ErrorMessage = "Khoảng hở đỉnh phải từ 0 đến 2 m.")]
    public double Margin { get; set; }
}

// ---------- Ra ----------

/// <summary>Trả về sau khi tạo phòng: frontend dùng id để mở trang Mặt bằng.</summary>
public record RoomCreatedViewModel(int Id);

/// <summary>Một dòng trong danh sách kho (GET /api/rooms).</summary>
public record RoomSummaryViewModel(
    int Id, string Name, double Width, double Depth, double Height, double GridSize,
    int RackCount, int SlotCount, int ObstacleCount);

/// <summary>Một phòng đầy đủ (GET /api/rooms/{id}): dùng cho trang Mặt bằng 2D và Kho 3D.</summary>
public record RoomViewModel(
    int Id, string Name, double Width, double Depth, double Height,
    double GridSize, double MinGap, double Margin, int Version,
    IReadOnlyList<ObstacleViewModel> Obstacles, IReadOnlyList<RackViewModel> Racks);

public record ObstacleViewModel(int Id, string Kind, double X, double Y, double Width, double Depth)
{
    public static ObstacleViewModel From(Obstacle o) => new(o.Id, o.Kind, o.X, o.Y, o.Width, o.Depth);
}

/// <summary>
/// Kệ theo dạng frontend đang dùng: mọi tầng thường giống nhau (Levels, LevelHeight, MaxWeight) + cờ HasTop.
/// Database lưu từng tầng ở bảng Levels; ở đây gộp lại: tầng thường đầu tiên đại diện cho chiều cao và tải trọng.
/// FootprintWidth/Depth: chỗ chiếm trên sàn (theo trục X/Y), đã tính xoay 90°.
/// </summary>
=======
// ViewModel = hình dạng JSON mà API trả về cho giao diện.
// Tên thuộc tính C# (FootprintWidth) tự đổi thành chữ thường đầu trong JSON (footprintWidth);
// giao diện đang đọc đúng các tên đó nên KHÔNG đổi tên trường nếu chưa sửa giao diện.

>>>>>>> main
public record RackViewModel(
    int Id, string Code, double X, double Y, int Rotation,
    int Levels, double LevelHeight, int Bays, double BayWidth, double Depth,
    bool HasTop, double MaxWeight, int? ProductTypeId,
<<<<<<< HEAD
    double FootprintWidth, double FootprintDepth, int SlotCount)
{
    /// <param name="rack">Kệ đã nạp kèm Levels.</param>
    /// <param name="slotCount">Số khung của kệ (đếm riêng để không phải nạp cả bảng Slots).</param>
    public static RackViewModel From(Rack rack, int slotCount)
    {
        var normal = rack.Levels.Where(l => !l.IsTop).OrderBy(l => l.Number).ToList();
        var first = normal.FirstOrDefault() ?? rack.Levels.FirstOrDefault();

        var length = rack.Bays * rack.BayWidth;   // chiều dài kệ, dọc theo các ô
        var (footprintWidth, footprintDepth) = rack.Rotation == 90
            ? (rack.Depth, length)                 // xoay 90°: dài chạy theo trục Y
            : (length, rack.Depth);

        return new RackViewModel(
            rack.Id, rack.Code, rack.X, rack.Y, rack.Rotation,
            normal.Count, first?.Height ?? 0, rack.Bays, rack.BayWidth, rack.Depth,
            rack.Levels.Any(l => l.IsTop), first?.MaxWeight ?? 0, rack.ProductTypeId,
            footprintWidth, footprintDepth, slotCount);
    }
}
=======
    double FootprintWidth, double FootprintDepth,   // chỗ kệ chiếm trên sàn (m), đã tính xoay
    int SlotCount);

public record ObstacleViewModel(int Id, string Kind, double X, double Y, double Width, double Depth);

public record RoomViewModel(
    int Id, string Name, double Width, double Depth, double GridSize, double MinGap, double Margin, int Version,
    IReadOnlyList<RackViewModel> Racks, IReadOnlyList<ObstacleViewModel> Obstacles);
>>>>>>> main
