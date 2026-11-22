using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Slot
{
    public int Id { get; set; }

    public int LevelId { get; set; }

    public int Position { get; set; }

    public string Code { get; set; } = null!;

    public bool Active { get; set; }

    public int? PalletId { get; set; }

    public virtual Level Level { get; set; } = null!;

    public virtual Pallet? Pallet { get; set; }
}
