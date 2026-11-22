namespace Warehouse.Api.Models;

public enum ObstacleKind
{
    Wall,
    Column,
}

public class Obstacle
{
    public int Id { get; set; }
    public ObstacleKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
}
