namespace Warehouse.Api.ViewModels;

using System.ComponentModel.DataAnnotations;
using Warehouse.Api.Models;

// ===== Đầu vào (frontend gửi lên) =====
// [Range]/[Required] chặn giá trị vô lý ngay ở cửa API; Models vẫn tự chặn lần nữa (ArgumentException).
// Giới hạn trên chỉ để chặn gõ nhầm, không phải luật nghiệp vụ. Mọi kích thước, toạ độ bằng mét.

/// <summary>Tạo hoặc sửa phòng bằng kích thước thật. Tường, cột thêm sau.</summary>
public class RoomRequest
{
    /// <summary>Tên phòng, không trùng phòng khác (không phân biệt hoa thường).</summary>
    [Required, StringLength(50)] public string Name { get; set; } = "";
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

/// <summary>Tường ngăn hoặc cột, hình chữ nhật bằng mét. X, Y là góc trên trái.</summary>
public class ObstacleRequest
{
    /// <summary>"Wall" (tường) hoặc "Column" (cột).</summary>
    [Required, AllowedValues("Wall", "Column")] public string Kind { get; set; } = "";
    [Range(0, 500)] public double X { get; set; }
    [Range(0, 500)] public double Y { get; set; }
    [Range(0.01, 500)] public double Width { get; set; }
    [Range(0.01, 500)] public double Depth { get; set; }
}

/// <summary>Kệ trong bản nháp. Id null = kệ mới vẽ, chưa có trong DB.</summary>
public class LayoutRackRequest : RackRequest
{
    public int? Id { get; set; }
}

/// <summary>Tường/cột trong bản nháp. Id null = mới vẽ.</summary>
public class LayoutObstacleRequest : ObstacleRequest
{
    public int? Id { get; set; }
}

/// <summary>
/// Cả bản nháp của một phòng (nút Lưu nháp): thông số phòng + toàn bộ tường, cột, kệ như người dùng đang thấy.
/// Version là số phiên bản phòng lúc tải về; lệch với DB thì bị chặn 409.
/// </summary>
public class LayoutRequest : RoomRequest
{
    public int Version { get; set; }
    [Required] public List<LayoutObstacleRequest> Obstacles { get; set; } = [];
    [Required] public List<LayoutRackRequest> Racks { get; set; } = [];
}

// ===== Đầu ra (API trả về) =====

/// <summary>Kết quả kiểm tra thử một kệ trong bản nháp (footprint đã tính xoay, để vẽ bóng mờ).</summary>
public record RackCheckViewModel(string Code, bool IsValid, string? Reason, double FootprintWidth, double FootprintDepth);

/// <summary>Kết quả kiểm tra thử cả bản nháp. IsValid = mọi kệ đều đặt được; Reason là lỗi đầu tiên.</summary>
public record LayoutCheckViewModel(bool IsValid, string? Reason, IReadOnlyList<RackCheckViewModel> Racks);

/// <summary>Id và mã của một kệ sau khi lưu (kệ mới được gán Id).</summary>
public record LayoutRackId(int Id, string Code);

/// <summary>
/// Chỉ trả những gì server tạo ra: Version mới, Id của kệ (theo mã), Id của tường/cột theo ĐÚNG THỨ TỰ gửi lên.
/// </summary>
public record LayoutSavedViewModel(int Version, IReadOnlyList<LayoutRackId> Racks, IReadOnlyList<int> ObstacleIds);

/// <summary>Chỉ trả Id: mọi số đo khác frontend vừa gửi lên, server không sửa gì.</summary>
public record RoomCreatedViewModel(int Id);

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

/// <summary>Một dòng trong danh sách phòng: đủ để hiện thẻ phòng, không kèm kệ và vật cản.</summary>
public record RoomSummaryViewModel(
    int Id, string Name, double Width, double Depth, double GridSize, int RackCount, int ObstacleCount, int SlotCount);

public record RoomViewModel(
    int Id, string Name, double Width, double Depth, double GridSize, double MinGap, double Margin, int Version,
    IReadOnlyList<RackViewModel> Racks, IReadOnlyList<ObstacleViewModel> Obstacles);
