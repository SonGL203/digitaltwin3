namespace Warehouse.Api.Models;

public class Slot
{
    public int Id { get; set; }
    public int RackId { get; set; }
    public string Code { get; set; } = "";
    public int Level { get; set; }
    public int Bay { get; set; }
    public bool IsTop { get; set; }
}
