namespace Warehouse.Api.Models;

/// <summary>
/// Phòng kho, mọi kích thước bằng mét. GridSize chỉ là bước "hít" khi kéo kệ trên giao diện (giống grid của Unity):
/// đổi GridSize không làm kệ, tường hay cột nào di chuyển hay đổi kích thước.
/// </summary>
public class Room
{
    public int Id { get; private set; }
    /// <summary>Tên phòng, đã cắt khoảng trắng. Không trùng giữa các phòng (service kiểm tra, vì cần biết mọi phòng).</summary>
    public string Name { get; private set; }
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

    public Room(string name, double width, double depth, double gridSize, double minGap, double margin)
    {
        Name = "";   // Configure gán thật; dòng này chỉ để trình biên dịch biết Name không null
        Configure(name, width, depth, gridSize, minGap, margin);
    }

    /// <summary>
    /// Đổi tên, kích thước, lưới, lối đi, khoảng hở. Kiểm tra HẾT rồi mới gán, để số sai không làm phòng bị sửa dở.
    /// Không kiểm tra kệ/vật cản còn nằm gọn không: cần nhiều đối tượng nên để service lo.
    /// </summary>
    public void Configure(string name, double width, double depth, double gridSize, double minGap, double margin)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên phòng không được rỗng.", nameof(name));
        if (width <= 0)
            throw new ArgumentException("Chiều dài phòng phải lớn hơn 0.", nameof(width));
        if (depth <= 0)
            throw new ArgumentException("Chiều rộng phòng phải lớn hơn 0.", nameof(depth));
        if (gridSize <= 0)
            throw new ArgumentException("Bước lưới phải lớn hơn 0.", nameof(gridSize));
        if (minGap < 0)
            throw new ArgumentException("Lối đi tối thiểu không được âm.", nameof(minGap));
        if (margin < 0)
            throw new ArgumentException("Khoảng hở không được âm.", nameof(margin));

        Name = name.Trim();
        Width = width;
        Depth = depth;
        GridSize = gridSize;
        MinGap = minGap;
        Margin = margin;
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

    /// <summary>Xoá tường/cột theo Id. Trả false nếu không có.</summary>
    public bool RemoveObstacle(int obstacleId) => Obstacles.RemoveAll(o => o.Id == obstacleId) > 0;
}
