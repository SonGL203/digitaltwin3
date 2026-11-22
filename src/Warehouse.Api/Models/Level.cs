using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Level
{
    public int Id { get; set; }

    public int RackId { get; set; }

    public int Number { get; set; }

    public double Height { get; set; }

    public double MaxWeight { get; set; }

    public bool IsTop { get; set; }

    public virtual Rack Rack { get; set; } = null!;

    public virtual ICollection<Slot> Slots { get; set; } = new List<Slot>();
}
