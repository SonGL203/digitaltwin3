namespace Warehouse.Api.Models;

/// <summary>
/// Phòng kho, mọi kích thước bằng mét. GridSize chỉ là bước "hít" khi kéo kệ trên giao diện (giống grid của Unity):
/// đổi GridSize không làm kệ, tường hay cột nào di chuyển hay đổi kích thước.
/// </summary>
public class Room
{
    public int Id { get; private set; }
    /// <summary>Chiều dài theo trục X (m).</summary>
    public double Width { get; private set; }
    /// <summary>Chiều rộng theo trục Y (m).</summary>
    public double Depth { get; private set; }
    /// <summary>Bước lưới để snap trên giao diện (m). Server không ép kệ phải nằm đúng lưới.</summary>
    public double GridSize { get; private set; }
    public double MinGap { get; private set; }
    public double Margin { get; private set; }

    public List<Rack> Racks { get; } = new();
    public List<Obstacle> Obstacles { get; } = new();

    public Room(double width, double depth, double gridSize, double minGap, double margin)
    {
        if (width <= 0)
            throw new ArgumentException("Chiều dài phòng phải lớn hơn 0.", nameof(width));
        if (depth <= 0)
            throw new ArgumentException("Chiều rộng phòng phải lớn hơn 0.", nameof(depth));
        if (minGap < 0)
            throw new ArgumentException("Lối đi tối thiểu không được âm.", nameof(minGap));
        if (margin < 0)
            throw new ArgumentException("Khoảng hở không được âm.", nameof(margin));

        Width = width;
        Depth = depth;
        MinGap = minGap;
        Margin = margin;
        SetGridSize(gridSize);
    }

    /// <summary>Sàn phòng: hình chữ nhật từ (0, 0). Kệ và vật cản phải nằm gọn trong đây.</summary>
    public Rect Bounds => new(0, 0, Width, Depth);

    /// <summary>Đổi bước lưới. Lúc nào cũng được, vì không có gì lưu theo ô.</summary>
    public void SetGridSize(double gridSize)
    {
        if (gridSize <= 0)
            throw new ArgumentException("Bước lưới phải lớn hơn 0.", nameof(gridSize));
        GridSize = gridSize;
    }

    /// <summary>
    /// Thêm tường/cột. Chỉ chặn vật cản nằm ngoài phòng (luật của riêng phòng).
    /// Không chặn vật cản đè lên kệ đang có: cần biết cả kệ lẫn vật cản nên để service lo, giống trước đây.
    /// </summary>
    public void AddObstacle(Obstacle obstacle)
    {
        ArgumentNullException.ThrowIfNull(obstacle);
        if (!obstacle.Bounds.IsInside(Bounds))
            throw new ArgumentException("Vật cản nằm ngoài phòng.", nameof(obstacle));
        Obstacles.Add(obstacle);
    }
}
