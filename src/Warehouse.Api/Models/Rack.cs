using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Rack
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public string Code { get; set; } = null!;

    public double X { get; set; }

    public double Y { get; set; }

    public int Rotation { get; set; }

    public int Bays { get; set; }

    public double BayWidth { get; set; }

    public double Depth { get; set; }

    public int? ProductTypeId { get; set; }

    public virtual ICollection<Level> Levels { get; set; } = new List<Level>();

    public virtual ProductType? ProductType { get; set; }

    public virtual Room Room { get; set; } = null!;
}
