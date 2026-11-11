namespace Warehouse.Api.ViewModels;

using System.ComponentModel.DataAnnotations;
using Warehouse.Api.Models;

// ===== Đầu vào (frontend gửi lên) =====
// [Range]/[Required] chặn giá trị vô lý ngay ở cửa API; Models vẫn tự chặn lần nữa (ArgumentException).
// Giới hạn trên chỉ để chặn gõ nhầm, không phải luật nghiệp vụ. Mọi kích thước, toạ độ bằng mét.

/// <summary>Tạo phòng trống bằng kích thước thật. Tường, cột thêm sau.</summary>
public class RoomRequest
{
    /// <summary>Chiều dài theo trục X (m).</summary>
    [Range(0.5, 500)] public double Width { get; set; }
    /// <summary>Chiều rộng theo trục Y (m).</summary>
    [Range(0.5, 500)] public double Depth { get; set; }
    /// <summary>Bước lưới để snap trên giao diện (m). Không ảnh hưởng vị trí kệ.</summary>
    [Range(0.05, 10)] public double GridSize { get; set; } = 0.5;
    /// <summary>Lối đi tối thiểu giữa hai kệ (m); 0 = cho kệ liền nhau.</summary>
    [Range(0, 20)] public double MinGap { get; set; }
    /// <summary>Khoảng hở đỉnh khi xếp pallet (m).</summary>
    [Range(0, 2)] public double Margin { get; set; }
}

/// <summary>Kệ muốn đặt (dùng cho cả kiểm tra thử và đặt thật). X, Y là góc trên trái (m).</summary>
public class RackRequest
{
    [Required, StringLength(20)] public string Code { get; set; } = "";
    [Range(0, 500)] public double X { get; set; }
    [Range(0, 500)] public double Y { get; set; }
    /// <summary>Chỉ 0 hoặc 90. Dùng AllowedValues thay vì Range vì Range(0, 90) vẫn cho 45 lọt qua.</summary>
    [AllowedValues(0, 90)] public int Rotation { get; set; }
    [Range(1, 20)] public int Levels { get; set; }
    [Range(0.1, 10)] public double LevelHeight { get; set; }
    [Range(1, 50)] public int Bays { get; set; }
    [Range(0.1, 10)] public double BayWidth { get; set; }
    [Range(0.1, 10)] public double Depth { get; set; }
    public bool HasTop { get; set; }
    [Range(1, 100000)] public double MaxWeight { get; set; }
    /// <summary>Null = kệ chung (nhận mọi loại hàng).</summary>
    public int? ProductTypeId { get; set; }
}

// ===== Đầu ra (API trả về) =====

/// <summary>Chỉ trả Id: mọi số đo khác frontend vừa gửi lên, server không sửa gì.</summary>
public record RoomCreatedViewModel(int Id);

/// <summary>Id do server gán; Code đã cắt khoảng trắng (" K1 " → "K1").</summary>
public record RackCreatedViewModel(int Id, string Code);

/// <summary>
/// FootprintWidth/FootprintDepth: kích thước kệ chiếm trên sàn theo trục X/Y (m), đã tính cả xoay 90°.
/// Trả sẵn để frontend chỉ vẽ, không tự đổi chỗ dài/sâu khi xoay (CLAUDE.md mục 3).
/// </summary>
public record RackViewModel(
    int Id, string Code, double X, double Y, int Rotation,
    int Levels, double LevelHeight, int Bays, double BayWidth, double Depth,
    bool HasTop, double MaxWeight, int? ProductTypeId,
    double FootprintWidth, double FootprintDepth, int SlotCount)
{
    public static RackViewModel From(Rack rack, int slotCount)
    {
        var b = rack.GetBounds();
        return new RackViewModel(
            rack.Id, rack.Code, rack.X, rack.Y, rack.Rotation,
            rack.Levels, rack.LevelHeight, rack.Bays, rack.BayWidth, rack.Depth,
            rack.HasTop, rack.MaxWeight, rack.ProductTypeId,
            b.Width, b.Depth, slotCount);
    }
}

/// <summary>Tường hoặc cột. Kind là chữ ("Wall"/"Column") để frontend đọc được, không phải đoán số 0/1.</summary>
public record ObstacleViewModel(int Id, string Kind, double X, double Y, double Width, double Depth)
{
    public static ObstacleViewModel From(Obstacle o)
        => new(o.Id, o.Kind.ToString(), o.X, o.Y, o.Width, o.Depth);
}

public record RoomViewModel(
    int Id, double Width, double Depth, double GridSize, double MinGap, double Margin,
    IReadOnlyList<RackViewModel> Racks, IReadOnlyList<ObstacleViewModel> Obstacles);

/// <summary>
/// Kết quả kiểm tra thử (bóng mờ). Đặt không được vẫn là 200 với IsValid = false: frontend chỉ việc tô đỏ và hiện Reason.
/// </summary>
public record PlacementCheckViewModel(bool IsValid, string? Reason, double FootprintWidth, double FootprintDepth);
