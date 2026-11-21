namespace Warehouse.Api.Models;

public enum ObstacleKind
{
    Wall,     // tường ngăn
    Column,   // cột
}

// Tường hoặc cột trong phòng: hình chữ nhật bằng mét.
public class Obstacle
{
    public int Id { get; set; }
    public ObstacleKind Kind { get; set; }
    public double X { get; set; }       // góc trên trái
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
}
