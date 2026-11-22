namespace Warehouse.Api.Models;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public double GridSize { get; set; }
    public double MinGap { get; set; }
    public double Margin { get; set; }
    public int Version { get; set; }
    public List<Rack> Racks { get; set; } = [];
    public List<Obstacle> Obstacles { get; set; } = [];
}
