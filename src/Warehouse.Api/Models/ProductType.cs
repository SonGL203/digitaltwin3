using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class ProductType
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Pallet> Pallets { get; set; } = new List<Pallet>();

    public virtual ICollection<Rack> Racks { get; set; } = new List<Rack>();
}
