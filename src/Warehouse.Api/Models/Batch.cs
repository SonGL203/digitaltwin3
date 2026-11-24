namespace Warehouse.Api.Models;

public class Batch
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public List<BatchLine> Lines { get; set; } = [];
}

public class BatchLine
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public PalletType Type { get; set; } = new();
    public int? ProductTypeId { get; set; }
    public List<Pallet> Pallets { get; set; } = [];
}

public class Pallet
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int BatchLineId { get; set; }
    public PalletType Type { get; set; } = new();
    public int? ProductTypeId { get; set; }
}
