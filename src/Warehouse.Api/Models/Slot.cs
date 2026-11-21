namespace Warehouse.Api.Models;

// Một khung (ô) trên kệ, nơi có thể đặt một pallet.
public class Slot
{
    public int Id { get; set; }
    public int RackId { get; set; }
    public string Code { get; set; } = "";     // K1-T1-R1 (tầng 1, ô 1); khung nóc: K1-N-R1
    public int Level { get; set; }             // tầng, đếm từ 1; khung nóc có Level = Levels + 1
    public int Bay { get; set; }               // ô, đếm từ 1
    public bool IsTop { get; set; }
}
