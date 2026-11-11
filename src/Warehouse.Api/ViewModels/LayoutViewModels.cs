namespace Warehouse.Api.ViewModels;

using System.ComponentModel.DataAnnotations;
using Warehouse.Api.Models;

// ===== Đầu vào (frontend gửi lên) =====
// [Range]/[Required] chặn giá trị vô lý ngay ở cửa API: [ApiController] tự trả 400 kèm tên trường sai,
// service không phải nhận dữ liệu rác. Models vẫn tự chặn lần nữa (ArgumentException) để không ai tạo được kệ sai.
// Giới hạn trên (200 ô, 20 tầng…) chỉ để chặn gõ nhầm, không phải luật nghiệp vụ.

/// <summary>Tạo phòng. Cells null = phòng trống toàn bộ.</summary>
public class RoomRequest
{
    [Range(1, 200)] public int Cols { get; set; }
    [Range(1, 200)] public int Rows { get; set; }
    /// <summary>Cạnh một ô lưới (m).</summary>
    [Range(0.1, 10)] public double CellSize { get; set; }
    /// <summary>Lối đi tối thiểu giữa hai kệ (m); 0 = cho kệ liền nhau.</summary>
    [Range(0, 20)] public double MinGap { get; set; }
    /// <summary>Khoảng hở đỉnh khi xếp pallet (m).</summary>
    [Range(0, 2)] public double Margin { get; set; }
    /// <summary>Chuỗi '.', '#', 'O' dài Cols × Rows, ô (x, y) ở vị trí y × Cols + x.</summary>
    public string? Cells { get; set; }
}

/// <summary>Kệ muốn đặt (dùng cho cả kiểm tra thử và đặt thật). X, Y là ô góc trên trái.</summary>
public class RackRequest
{
    [Required, StringLength(20)] public string Code { get; set; } = "";
    [Range(0, 199)] public int X { get; set; }
    [Range(0, 199)] public int Y { get; set; }
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
// Trả sẵn footprint để frontend chỉ vẽ, không tự tính lại (CLAUDE.md mục 3).

// Kết quả tạo mới: chỉ trả những gì server tạo ra hoặc đã sửa so với dữ liệu frontend gửi lên.
// Phần còn lại frontend đã có sẵn (chính nó vừa gửi), trả lại là thừa.

/// <summary>Id do server gán; Cells server tự sinh khi frontend gửi null (phòng trống).</summary>
public record RoomCreatedViewModel(int Id, string Cells);

/// <summary>
/// Id do server gán; Code là mã đã chuẩn hoá (cắt khoảng trắng " K1 " → "K1").
/// Không trả footprint: frontend đã có từ lần gọi validate ngay trước khi thả kệ.
/// </summary>
public record RackCreatedViewModel(int Id, string Code);

public record RackViewModel(
    int Id, string Code, int X, int Y, int Rotation,
    int Levels, double LevelHeight, int Bays, double BayWidth, double Depth,
    bool HasTop, double MaxWeight, int? ProductTypeId,
    int FootprintCols, int FootprintRows, int SlotCount)
{
    public static RackViewModel From(Rack rack, double cellSize, int slotCount)
    {
        var f = rack.GetFootprint(cellSize);
        return new RackViewModel(
            rack.Id, rack.Code, rack.X, rack.Y, rack.Rotation,
            rack.Levels, rack.LevelHeight, rack.Bays, rack.BayWidth, rack.Depth,
            rack.HasTop, rack.MaxWeight, rack.ProductTypeId,
            f.Cols, f.Rows, slotCount);
    }
}

public record RoomViewModel(
    int Id, int Cols, int Rows, double CellSize, string Cells, double MinGap, double Margin,
    IReadOnlyList<RackViewModel> Racks);

/// <summary>
/// Kết quả kiểm tra thử (bóng mờ). Đặt không được vẫn là một câu trả lời bình thường (200, IsValid = false),
/// không phải lỗi HTTP: frontend chỉ việc tô đỏ và hiện Reason.
/// </summary>
public record PlacementCheckViewModel(bool IsValid, string? Reason, int FootprintCols, int FootprintRows);
