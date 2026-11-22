using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Pallet
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public int BatchId { get; set; }

    public int PalletTypeId { get; set; }

    public int? ProductTypeId { get; set; }

    public virtual Batch Batch { get; set; } = null!;

    public virtual PalletType PalletType { get; set; } = null!;

    public virtual ProductType? ProductType { get; set; }

    public virtual Slot? Slot { get; set; }
}
