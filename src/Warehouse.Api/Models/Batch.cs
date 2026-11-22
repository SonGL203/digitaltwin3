using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Batch
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public DateTime ReceivedAt { get; set; }

    public virtual ICollection<Pallet> Pallets { get; set; } = new List<Pallet>();
}
