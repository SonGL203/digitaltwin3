namespace Warehouse.Api.ViewModels;

public class RoomInfoViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public List<RackInfoViewModel> Racks { get; set; } = [];
}

public class RackInfoViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Rotation { get; set; }
    public int Bays { get; set; }
    public double BayWidth { get; set; }
    public double Depth { get; set; }
    public int? ProductTypeId { get; set; }
    public List<LevelInfoViewModel> Levels { get; set; } = [];
}

public class LevelInfoViewModel
{
    public int Id { get; set; }
    public int Number { get; set; }
    public double Height { get; set; }
    public double MaxWeight { get; set; }
    public bool IsTop { get; set; }
}

public class SlotViewModel
{
    public int Id { get; set; }
    public int RackId { get; set; }
    public int LevelId { get; set; }
    public string Code { get; set; } = "";
    public int Level { get; set; }
    public int Bay { get; set; }
    public bool IsTop { get; set; }
    public bool Active { get; set; }
    public int? PalletId { get; set; }
    public string? PalletCode { get; set; }
    public string? TypeCode { get; set; }
    public string? Color { get; set; }
    public double? PalletAlongBay { get; set; }
    public double? PalletAcrossDepth { get; set; }
    public double? PalletHeight { get; set; }
    public int? ProductTypeId { get; set; }
    public string? ProductTypeName { get; set; }
}
