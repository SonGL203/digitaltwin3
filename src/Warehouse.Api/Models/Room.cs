namespace Warehouse.Api.Models;
public class Room
{
    public const char EmptyCell = '.';
    public const char WallCell = '#';
    public const char ObstacleCell = 'O';

    public int Id { get; private set; }
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public double CellSize { get; private set; }
    public string Cells { get; private set; }
    public double MinGap { get; private set; }
    public double Margin { get; private set; }

    public List<Rack> Racks { get; } = new();

    public Room(int cols, int rows, double cellSize, double minGap, double margin, string? cells = null)
    {
        if (cols <= 0)
            throw new ArgumentException("Số cột phải lớn hơn 0.", nameof(cols));
        if (rows <= 0)
            throw new ArgumentException("Số hàng phải lớn hơn 0.", nameof(rows));
        if (cellSize <= 0)
            throw new ArgumentException("Cạnh ô lưới phải lớn hơn 0.", nameof(cellSize));
        if (minGap < 0)
            throw new ArgumentException("Lối đi tối thiểu không được âm.", nameof(minGap));
        if (margin < 0)
            throw new ArgumentException("Khoảng hở không được âm.", nameof(margin));

        cells ??= new string(EmptyCell, cols * rows);
        if (cells.Length != cols * rows)
            throw new ArgumentException("Độ dài Cells phải bằng Cols × Rows.", nameof(cells));
        if (cells.Any(c => c != EmptyCell && c != WallCell && c != ObstacleCell))
            throw new ArgumentException("Cells chỉ được chứa '.', '#' hoặc 'O'.", nameof(cells));

        Cols = cols;
        Rows = rows;
        CellSize = cellSize;
        MinGap = minGap;
        Margin = margin;
        Cells = cells;
    }

    /// <summary>Chiều rộng thật của phòng theo trục X (m).</summary>
    public double WidthMeters => Cols * CellSize;

    /// <summary>Chiều sâu thật của phòng theo trục Y của lưới (m).</summary>
    public double DepthMeters => Rows * CellSize;

    /// <summary>Ô (x, y) có nằm trong lưới không. x là cột, y là hàng, đều đếm từ 0.</summary>
    public bool IsInside(int x, int y) => x >= 0 && x < Cols && y >= 0 && y < Rows;

    /// <summary>
    /// Loại của ô (x, y). Ném lỗi nếu ngoài lưới, để nơi gọi phải kiểm tra IsInside trước thay vì đọc nhầm ô khác.
    /// </summary>
    public CellType CellAt(int x, int y) => FromChar(Cells[IndexOf(x, y)]);

    /// <summary>Đặt loại cho ô (x, y): vẽ hoặc xoá tường, vật cản. Chỉ ô đó đổi, độ dài Cells giữ nguyên.</summary>
    public void SetCell(int x, int y, CellType type)
    {
        int index = IndexOf(x, y);
        var chars = Cells.ToCharArray();
        chars[index] = ToChar(type);
        Cells = new string(chars);
    }

    /// <summary>Ô là tường hoặc vật cản (kệ không được đè). Ném lỗi nếu ô ngoài lưới.</summary>
    public bool IsBlocked(int x, int y) => CellAt(x, y) != CellType.Empty;

    // Vị trí của ô (x, y) trong chuỗi Cells: hàng nhân số cột, cộng cột. Chỗ duy nhất tính công thức này.
    private int IndexOf(int x, int y)
    {
        if (!IsInside(x, y))
            throw new ArgumentException($"Ô ({x}, {y}) nằm ngoài phòng {Cols} × {Rows}.");

        return y * Cols + x;
    }

    private static CellType FromChar(char c) => c switch
    {
        WallCell => CellType.Wall,
        ObstacleCell => CellType.Obstacle,
        _ => CellType.Empty,
    };

    private static char ToChar(CellType type) => type switch
    {
        CellType.Wall => WallCell,
        CellType.Obstacle => ObstacleCell,
        _ => EmptyCell,
    };
}
