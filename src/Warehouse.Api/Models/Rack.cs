namespace Warehouse.Api.Models;

public class Rack
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Rotation { get; set; }
    public int Levels { get; set; }
    public double LevelHeight { get; set; }
    public int Bays { get; set; }
    public double BayWidth { get; set; }
    public double Depth { get; set; }
    public bool HasTop { get; set; }
    public double MaxWeight { get; set; }
    public int? ProductTypeId { get; set; }

    /// <summary>Mã khung: K1-T2-R3; tầng lớn hơn số tầng là nóc: K1-N-R3.</summary>
    public string SlotCode(int level, int bay) =>
        level > Levels ? $"{Code}-N-R{bay}" : $"{Code}-T{level}-R{bay}";

    /// <summary>Khác mã, số tầng, số ô hoặc nóc: danh sách khung (và mã khung) phải sinh lại.</summary>
    public bool SlotsDifferFrom(Rack other) =>
        Code != other.Code || Levels != other.Levels || Bays != other.Bays || HasTop != other.HasTop;

    /// <summary>Khác cao tầng hoặc tải khung: khung giữ nguyên, chỉ sửa số liệu tầng.</summary>
    public bool LevelValuesDifferFrom(Rack other) =>
        Math.Abs(LevelHeight - other.LevelHeight) > 1e-9 || Math.Abs(MaxWeight - other.MaxWeight) > 1e-9;

    /// <summary>Khác rộng ô hoặc sâu kệ: khung đổi kích thước.</summary>
    public bool SizeDiffersFrom(Rack other) =>
        Math.Abs(BayWidth - other.BayWidth) > 1e-9 || Math.Abs(Depth - other.Depth) > 1e-9;

    public Rect GetBounds()
    {
        double length = Bays * BayWidth;
        return Rotation == 90
            ? new Rect(X, Y, Depth, length)
            : new Rect(X, Y, length, Depth);
    }
}
