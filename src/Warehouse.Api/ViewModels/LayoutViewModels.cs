namespace Warehouse.Api.ViewModels;

/// <summary>
/// Cả bản nháp mặt bằng (body của POST /layout/validate và PUT /layout), đúng dạng toLayoutRequest ở frontend.
/// Id = null là đối tượng mới vẽ. Kệ, tường/cột có trong database mà không có ở đây thì bị xoá khi lưu.
/// </summary>
public class SaveLayoutRequest
{
    public string Name { get; set; } = "";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public double GridSize { get; set; }
    public double MinGap { get; set; }
    public double Margin { get; set; }

    /// <summary>Phiên bản lúc tải bản nháp; lệch với database thì không cho lưu.</summary>
    public int Version { get; set; }

    public List<ObstacleRequest> Obstacles { get; set; } = [];
    public List<RackRequest> Racks { get; set; } = [];
}

public class ObstacleRequest
{
    public int? Id { get; set; }
    public string Kind { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
}

public class RackRequest
{
    public int? Id { get; set; }
    public string Code { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Rotation { get; set; }
    public int Levels { get; set; }
    public double LevelHeight { get; set; }
    public int Bays { get; set; }
    public double BayWidth { get; set; }
    public double Depth { get; set; }
    public bool HasTop { get; set; }
    public double MaxWeight { get; set; }
    public int? ProductTypeId { get; set; }
}

/// <summary>
/// Tiến độ Lưu nháp chạy nền. Status: Queued, Running, Done, Conflict, Failed (Error là câu lý do).
/// Total: số kệ phải sinh tầng, khung; Built: số kệ đã sinh xong.
/// </summary>
public record LayoutSaveJobViewModel(int RoomId, string Status, int Total, int Built, string? Error);

/// <summary>Kết quả kiểm tra một kệ. Footprint = chỗ chiếm trên sàn (đã tính xoay).</summary>
public record RackCheckViewModel(string Code, bool IsValid, string? Reason, double FootprintWidth, double FootprintDepth);

/// <summary>Kết quả kiểm tra cả bản nháp. Reason = lỗi đầu tiên ("K3: Kệ đè cột…"), null là hợp lệ.</summary>
public record LayoutCheckViewModel(bool IsValid, string? Reason, IReadOnlyList<RackCheckViewModel> Racks);
