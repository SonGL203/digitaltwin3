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
}
