namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

/// <summary>
/// Kiểm tra đặt một kệ vào phòng. Hàm thuần: không đọc store, không sửa phòng hay kệ.
/// LayoutService gọi trước khi lưu kệ; API xem trước (bóng mờ xanh/đỏ) cũng gọi hàm này.
/// </summary>
public static class PlacementValidator
{
    // Hình chữ nhật trên lưới: X, Y là ô góc trên trái; Cols, Rows là số ô theo trục X, Y.
    private readonly record struct Area(int X, int Y, int Cols, int Rows)
    {
        public bool Overlaps(Area other)
            => X < other.X + other.Cols && other.X < X + Cols
            && Y < other.Y + other.Rows && other.Y < Y + Rows;

        public Area Inflate(int cells) => new(X - cells, Y - cells, Cols + 2 * cells, Rows + 2 * cells);
    }

    /// <summary>
    /// Trả về null nếu đặt được, ngược lại trả câu lý do. Kiểm tra theo thứ tự từ vi phạm nặng đến nhẹ để lý do báo ra
    /// luôn là lý do thật: ngoài sàn → đè tường/vật cản → đè kệ khác → thiếu lối đi.
    /// Kệ khác lấy từ room.Racks, bỏ qua kệ cùng mã với kệ đang kiểm tra (di chuyển hay xoay kệ đã có không tự đè mình).
    /// Lối đi chỉ áp dụng giữa kệ với kệ (kệ sát tường vẫn được) và cho mọi cặp kệ; MinGap = 0 thì kệ được đặt liền nhau.
    /// </summary>
    public static string? Validate(Room room, Rack rack)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(rack);

        var footprint = rack.GetFootprint(room.CellSize);
        var area = new Area(rack.X, rack.Y, footprint.Cols, footprint.Rows);

        // 1. Ngoài sàn: hình chữ nhật nằm trong lưới khi cả ô góc trên trái và ô góc dưới phải đều trong lưới.
        //    Phải kiểm tra trước bước 2 vì ô ngoài lưới không đọc được (CellAt ném lỗi).
        if (!room.IsInside(area.X, area.Y) || !room.IsInside(area.X + area.Cols - 1, area.Y + area.Rows - 1))
            return "Kệ nằm ngoài sàn.";

        // 2. Đè tường hoặc vật cản: tường rải rác trong lưới nên phải hỏi từng ô kệ chiếm (hàng trước, cột sau).
        for (int y = area.Y; y < area.Y + area.Rows; y++)
        {
            for (int x = area.X; x < area.X + area.Cols; x++)
            {
                var cell = room.CellAt(x, y);
                if (cell == CellType.Wall)
                    return $"Kệ đè tường ở ô ({x}, {y}).";
                if (cell == CellType.Obstacle)
                    return $"Kệ đè vật cản ở ô ({x}, {y}).";
            }
        }

        var others = room.Racks
            .Where(r => !string.Equals(r.Code, rack.Code, StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                var f = r.GetFootprint(room.CellSize);
                return (Rack: r, Area: new Area(r.X, r.Y, f.Cols, f.Rows));
            })
            .ToList();

        // 3. Đè kệ khác. Làm xong cho mọi kệ rồi mới sang bước 4, để đè kệ này luôn được báo trước thiếu lối đi với kệ kia.
        foreach (var other in others)
        {
            if (area.Overlaps(other.Area))
                return $"Kệ đè lên {other.Rack.Code}.";
        }

        // 4. Thiếu lối đi: lối đi rộng MinGap mét = ít nhất g ô trống giữa hai kệ, nên phồng kệ ra g ô mỗi phía
        //    rồi xem có chạm kệ khác không.
        int gapCells = Math.Max(0, (int)Math.Ceiling(room.MinGap / room.CellSize - Tolerance.Epsilon));
        if (gapCells > 0)
        {
            var inflated = area.Inflate(gapCells);
            foreach (var other in others)
            {
                if (inflated.Overlaps(other.Area))
                    return $"Thiếu lối đi với {other.Rack.Code}.";
            }
        }

        return null;
    }
}
