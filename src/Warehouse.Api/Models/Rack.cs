namespace Warehouse.Api.Models;

public class Rack
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Rotation { get; set; }
    public int Levels { get; set; }
    public double LevelHeight { get; set; }
    public int Bays { get; set; }
    public double BayWidth { get; set; }
    public double Depth { get; set; }
    public bool HasTop { get; set; }
    public double MaxWeight { get; set; }
    public int? ProductTypeId { get; set; }

    public Rect GetBounds()
    {
        double length = Bays * BayWidth;
        return Rotation == 90
            ? new Rect(X, Y, Depth, length)
            : new Rect(X, Y, length, Depth);
    }
}
