namespace Warehouse.Api.Rules;

using System.Globalization;
using Warehouse.Api.Models;

/// <summary>Kết quả kiểm tra một kệ: Reason = null là đặt được.</summary>
public record RackCheck(Rack Rack, string? Reason);

/// <summary>Kết quả kiểm tra cả mặt bằng. Reason = lỗi đầu tiên (của phòng, tường/cột hoặc "K3: lý do"), null là hợp lệ.</summary>
public record LayoutCheck(string? Reason, IReadOnlyList<RackCheck> Racks)
{
    public bool IsValid => Reason == null;
}

/// <summary>
/// Luật đặt kệ trên mặt bằng. Hàm thuần: chỉ nhận Room (đã gắn kệ, tường/cột của bản nháp), không đọc database.
/// Toạ độ mét, gốc (0; 0) ở góc trên trái phòng. Hai hình chạm mép nhau không tính là đè.
/// </summary>
public static class PlacementValidator
{
    /// <summary>Sai số so sánh số thực (mét): 0,1 + 0,2 vẫn bằng 0,3.</summary>
    public const double Eps = 1e-6;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Hiện số mét kiểu Việt Nam: 1,2.</summary>
    public static string M(double value) => value.ToString("0.###", Vi);

    // ---------- số liệu của phòng ----------

    public static string? CheckRoomValues(
        string name, double width, double depth, double height, double gridSize, double minGap, double margin)
    {
        name = name.Trim();
        if (name.Length == 0) return "Chưa nhập tên kho.";
        if (name.Length > 50) return "Tên kho tối đa 50 ký tự.";
        if (width < 0.5 || width > 500) return "Chiều dài phải từ 0,5 đến 500 m.";
        if (depth < 0.5 || depth > 500) return "Chiều rộng phải từ 0,5 đến 500 m.";
        if (height < 1 || height > 50) return "Chiều cao trần phải từ 1 đến 50 m.";
        if (gridSize < 0.05 || gridSize > 10) return "Bước lưới phải từ 0,05 đến 10 m.";
        if (minGap < 0 || minGap > 20) return "Lối đi tối thiểu phải từ 0 đến 20 m.";
        if (margin < 0 || margin > 2) return "Khoảng hở đỉnh phải từ 0 đến 2 m.";
        return null;
    }

    // ---------- tường, cột ----------

    public static string? CheckObstacle(Room room, Obstacle o)
    {
        var what = o.Kind == ObstacleKind.Wall ? "Tường" : "Cột";
        var at = $"tại ({M(o.X)}; {M(o.Y)})";
        if (o.Width <= 0 || o.Depth <= 0) return $"{what} {at}: dài và rộng phải lớn hơn 0.";
        if (!InsideFloor(room, new Rect(o.X, o.Y, o.Width, o.Depth))) return $"{what} {at} nằm ngoài phòng.";
        return null;
    }

    // ---------- kệ ----------

    /// <summary>Số liệu riêng của kệ (chưa xét vị trí).</summary>
    public static string? CheckRackValues(Rack rack, double margin)
    {
        if (string.IsNullOrWhiteSpace(rack.Code)) return "Chưa có mã kệ.";
        if (rack.Code.Trim().Length > 20) return "Mã kệ tối đa 20 ký tự.";
        if (rack.Rotation is not (0 or 90)) return "Góc xoay chỉ được 0 hoặc 90 độ.";
        if (rack.Levels < 1 || rack.Levels > 50) return "Số tầng phải từ 1 đến 50.";
        if (rack.LevelHeight < 0.1 || rack.LevelHeight > 20) return "Cao tầng phải từ 0,1 đến 20 m.";
        if (rack.LevelHeight <= margin + Eps)
            return $"Cao tầng {M(rack.LevelHeight)} m phải lớn hơn khoảng hở đỉnh {M(margin)} m.";
        if (rack.Bays < 1 || rack.Bays > 100) return "Số ô hàng phải từ 1 đến 100.";
        if (rack.BayWidth < 0.1 || rack.BayWidth > 20) return "Rộng ô phải từ 0,1 đến 20 m.";
        if (rack.Depth < 0.1 || rack.Depth > 20) return "Sâu kệ phải từ 0,1 đến 20 m.";
        if (rack.MaxWeight <= 0 || rack.MaxWeight > 100_000) return "Tải khung phải lớn hơn 0 và tối đa 100 000 kg.";
        return null;
    }

    /// <summary>Vị trí của kệ: trong sàn, không vượt trần, không đè tường/cột/kệ khác, đủ lối đi tới kệ khác.</summary>
    public static string? CheckRackPlacement(Room room, Rack rack)
    {
        var box = rack.GetBounds();
        if (!InsideFloor(room, box)) return "Kệ ra ngoài sàn phòng.";

        double rackHeight = rack.Levels * rack.LevelHeight;
        if (rackHeight > room.Height + Eps)
            return $"Kệ cao {M(rackHeight)} m vượt trần {M(room.Height)} m.";

        foreach (var o in room.Obstacles)
        {
            if (Overlaps(box, new Rect(o.X, o.Y, o.Width, o.Depth)))
                return $"Kệ đè {(o.Kind == ObstacleKind.Wall ? "tường" : "cột")} tại ({M(o.X)}; {M(o.Y)}).";
        }

        foreach (var other in room.Racks)
        {
            if (ReferenceEquals(other, rack)) continue;
            var otherBox = other.GetBounds();
            if (Overlaps(box, otherBox)) return $"Kệ đè kệ {other.Code}.";

            double gap = Gap(box, otherBox);
            if (gap < room.MinGap - Eps)
                return $"Cách kệ {other.Code} chỉ {M(gap)} m, cần lối đi ít nhất {M(room.MinGap)} m.";
        }
        return null;
    }

    /// <summary>
    /// Kiểm tra cả mặt bằng. productTypeIds: các loại hàng đang có (null = không kiểm tra).
    /// Mỗi kệ luôn có một dòng kết quả (để frontend lấy kích thước chiếm chỗ), kể cả khi phòng sai.
    /// </summary>
    public static LayoutCheck CheckLayout(Room room, IReadOnlySet<int>? productTypeIds = null)
    {
        string? roomReason = CheckRoomValues(room.Name, room.Width, room.Depth, room.Height, room.GridSize, room.MinGap, room.Margin);
        if (roomReason == null)
        {
            foreach (var o in room.Obstacles)
            {
                roomReason = CheckObstacle(room, o);
                if (roomReason != null) break;
            }
        }

        var duplicateCodes = room.Racks
            .GroupBy(r => r.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var checks = room.Racks.Select(rack =>
        {
            string? reason = CheckRackValues(rack, room.Margin);
            if (reason == null && duplicateCodes.Contains(rack.Code.Trim()))
                reason = $"Trùng mã kệ {rack.Code.Trim()}.";
            if (reason == null && rack.ProductTypeId is int typeId && productTypeIds != null && !productTypeIds.Contains(typeId))
                reason = $"Loại hàng Id {typeId} không tồn tại.";
            if (reason == null && roomReason == null)
                reason = CheckRackPlacement(room, rack);
            return new RackCheck(rack, reason);
        }).ToList();

        string? first = roomReason
            ?? checks.Where(c => c.Reason != null).Select(c => $"{c.Rack.Code}: {c.Reason}").FirstOrDefault();
        return new LayoutCheck(first, checks);
    }

    // ---------- hình học ----------

    public static bool InsideFloor(Room room, Rect r) =>
        r.X >= -Eps && r.Y >= -Eps
        && r.X + r.Width <= room.Width + Eps
        && r.Y + r.Depth <= room.Depth + Eps;

    /// <summary>Hai hình chữ nhật có phần chung diện tích dương (chỉ chạm mép thì không tính).</summary>
    public static bool Overlaps(Rect a, Rect b) =>
        a.X < b.X + b.Width - Eps && b.X < a.X + a.Width - Eps
        && a.Y < b.Y + b.Depth - Eps && b.Y < a.Y + a.Depth - Eps;

    /// <summary>
    /// Khoảng trống giữa hai hình không đè nhau: khoảng hở lớn nhất theo trục X hoặc Y.
    /// Hai kệ lệch chéo nhau thì lối đi là khe rộng hơn trong hai khe.
    /// </summary>
    public static double Gap(Rect a, Rect b)
    {
        double dx = Math.Max(0, Math.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width)));
        double dy = Math.Max(0, Math.Max(b.Y - (a.Y + a.Depth), a.Y - (b.Y + b.Depth)));
        return Math.Max(dx, dy);
    }
}
