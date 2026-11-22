using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Room
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public double Width { get; set; }

    public double Depth { get; set; }

    public double Height { get; set; }

    public double GridSize { get; set; }

    public double MinGap { get; set; }

    public double Margin { get; set; }

    public int Version { get; set; }

    public virtual ICollection<Obstacle> Obstacles { get; set; } = new List<Obstacle>();

    public virtual ICollection<Rack> Racks { get; set; } = new List<Rack>();
}
