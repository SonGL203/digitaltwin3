using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Obstacle
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public string Kind { get; set; } = null!;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Depth { get; set; }

    public virtual Room Room { get; set; } = null!;
}
