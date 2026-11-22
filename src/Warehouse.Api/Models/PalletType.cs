using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class PalletType
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public double Length { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double? Weight { get; set; }

    public string Color { get; set; } = null!;

    public virtual ICollection<Pallet> Pallets { get; set; } = new List<Pallet>();
}
