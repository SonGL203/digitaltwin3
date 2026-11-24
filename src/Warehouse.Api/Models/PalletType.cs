namespace Warehouse.Api.Models;

public class PalletType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double? Weight { get; set; }
    public string Color { get; set; } = "";
}
