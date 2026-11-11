namespace Warehouse.Api.Rules;

using System.Globalization;
using Warehouse.Api.Models;

/// <summary>
/// Kiểm tra đặt một kệ vào phòng. Hàm thuần: không đọc store, không sửa phòng hay kệ.
/// Mọi thứ là hình chữ nhật bằng mét (Rect); so sánh qua Tolerance nên kệ chạm sát nhau không bị coi là đè.
/// </summary>
public static class PlacementValidator
{
    /// <summary>
    /// Trả về null nếu đặt được, ngược lại trả câu lý do. Thứ tự từ nặng đến nhẹ để lý do luôn là lý do thật:
    /// ngoài sàn → đè tường/cột → đè kệ khác → thiếu lối đi.
    /// Bỏ qua kệ cùng mã (không phân biệt hoa thường) để dời/xoay kệ đã có không tự đè mình.
    /// </summary>
    public static string? Validate(Room room, Rack rack)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(rack);

        var bounds = rack.GetBounds();

        // 1. Ngoài sàn. Không còn "ô ngoài lưới ném lỗi", nhưng vẫn kiểm tra đầu tiên vì đó là lý do nặng nhất.
        if (!bounds.IsInside(room.Bounds))
            return "Kệ nằm ngoài sàn.";

        // 2. Đè tường hoặc cột: báo vật cản đầu tiên bị đè, kèm vị trí để người dùng tìm thấy trên hình.
        foreach (var obstacle in room.Obstacles)
        {
            if (bounds.Overlaps(obstacle.Bounds))
            {
                var what = obstacle.Kind == ObstacleKind.Wall ? "tường" : "cột";
                return $"Kệ đè {what} tại ({Meters(obstacle.X)}; {Meters(obstacle.Y)}) m.";
            }
        }

        var others = room.Racks
            .Where(r => !string.Equals(r.Code, rack.Code, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Rack: r, Bounds: r.GetBounds()))
            .ToList();

        // 3. Đè kệ khác. Xong hết rồi mới sang bước 4, để "đè K1" luôn được báo trước "thiếu lối đi với K2".
        foreach (var other in others)
        {
            if (bounds.Overlaps(other.Bounds))
                return $"Kệ đè lên {other.Rack.Code}.";
        }

        // 4. Thiếu lối đi: chỉ giữa kệ với kệ (sát tường vẫn được). Thiếu khi khoảng trống theo CẢ HAI trục
        //    đều nhỏ hơn MinGap; chỉ cần một trục đủ rộng là người và xe đi qua được.
        //    Tương đương cách cũ "phồng kệ ra MinGap mọi phía rồi xem có chạm kệ khác".
        foreach (var other in others)
        {
            var (dx, dy) = bounds.GapTo(other.Bounds);
            if (Tolerance.LessThan(dx, room.MinGap) && Tolerance.LessThan(dy, room.MinGap))
                return $"Thiếu lối đi với {other.Rack.Code}.";
        }

        return null;
    }

    // 6.5 → "6.5", 2 → "2": gọn và không phụ thuộc ngôn ngữ của máy chạy server.
    private static string Meters(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
