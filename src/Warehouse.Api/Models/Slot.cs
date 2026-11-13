namespace Warehouse.Api.Models;

/// <summary>
/// Một khung kệ, sinh từ cấu hình kệ (mã dạng K1-T1-R1, nóc là K1-N-R1).
/// Kích thước khung không lưu: lấy từ Rack (rộng = BayWidth, sâu = Depth, cao = LevelHeight; nóc không giới hạn cao).
/// AssignedSize (nhãn) và PalletId luôn cùng có giá trị hoặc cùng null; sẽ do Place()/Remove() ghi.
/// </summary>
public class Slot
{
    public int Id { get; private set; }
    public int RackId { get; private set; }
    public string Code { get; private set; }
    public int Level { get; private set; }
    public int Bay { get; private set; }
    public bool IsTop { get; private set; }
    public string? AssignedSize { get; private set; }
    public int? PalletId { get; private set; }

    public Slot(int rackId, string code, int level, int bay, bool isTop)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Mã khung không được rỗng.", nameof(code));
        if (level < 1)
            throw new ArgumentException("Tầng đếm từ 1.", nameof(level));
        if (bay < 1)
            throw new ArgumentException("Ô đếm từ 1.", nameof(bay));

        RackId = rackId;
        Code = code.Trim();
        Level = level;
        Bay = bay;
        IsTop = isTop;
    }

    /// <summary>Khung đang trống (không có pallet).</summary>
    public bool IsEmpty => PalletId is null;

    /// <summary>
    /// Khung này nhận được pallet này không? Trả về null nếu được, ngược lại trả câu lý do.
    /// Nhận Rack làm tham số vì kích thước khung nằm ở kệ (Slot không lưu). Pallet được xoay 90° nếu xoay thì vừa.
    /// Khung nóc không giới hạn chiều cao. margin là khoảng hở giữa đỉnh pallet và thanh ngang phía trên (Room.Margin).
    /// Thứ tự kiểm tra: loại hàng, đáy, chiều cao, tải trọng. Không kiểm tra khung trống hay không (việc của Allocator và Place).
    /// Loại hàng: kệ chung (ProductTypeId null) nhận mọi pallet; kệ chuyên chỉ nhận pallet đúng loại của nó,
    /// pallet chưa phân loại chỉ vào được kệ chung.
    /// </summary>
    public string? CanHold(Rack rack, Pallet pallet, double margin)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(pallet);
        if (margin < 0)
            throw new ArgumentException("Khoảng hở không được âm.", nameof(margin));

        if (rack.ProductTypeId is not null && rack.ProductTypeId != pallet.ProductTypeId)
            return "Kệ chỉ dành cho loại hàng khác.";

        var size = pallet.Size;

        bool fitsFloor =
            (Tolerance.AtMost(size.Length, rack.BayWidth) && Tolerance.AtMost(size.Width, rack.Depth))
            || (Tolerance.AtMost(size.Width, rack.BayWidth) && Tolerance.AtMost(size.Length, rack.Depth));
        if (!fitsFloor)
            return "Pallet rộng hơn khung.";

        if (!IsTop && !Tolerance.AtMost(size.Height, rack.LevelHeight - margin))
            return "Pallet cao hơn khung.";

        if (size.Weight is double weight && !Tolerance.AtMost(weight, rack.MaxWeight))
            return "Pallet nặng hơn tải trọng khung.";

        return null;
    }

    /// <summary>
    /// Pallet nằm trong khung theo chiều nào, để vẽ đúng hình: (dọc ô, theo chiều sâu kệ) bằng mét.
    /// Cùng thứ tự CanHold thử: vừa mà không xoay thì giữ nguyên (dài dọc ô), không vừa thì xoay 90°.
    /// Không kiểm tra pallet có vừa khung không (việc của CanHold); frontend không tự tính chiều xoay.
    /// </summary>
    public static (double AlongBay, double AcrossDepth) PalletFootprint(Rack rack, PalletSize size)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(size);
        return Tolerance.AtMost(size.Length, rack.BayWidth) && Tolerance.AtMost(size.Width, rack.Depth)
            ? (size.Length, size.Width)
            : (size.Width, size.Length);
    }

    /// <summary>
    /// Đặt pallet vào khung: ghi PalletId và nhãn AssignedSize (mã type của pallet) cùng lúc.
    /// Không tự gọi CanHold; chỉ chặn khung đã có pallet.
    /// </summary>
    public void Place(Pallet pallet)
    {
        ArgumentNullException.ThrowIfNull(pallet);
        if (!IsEmpty)
            throw new InvalidOperationException($"Khung {Code} đã có pallet.");

        PalletId = pallet.Id;
        AssignedSize = pallet.Size.Code;
    }

    /// <summary>Lấy pallet ra: xoá cả PalletId và nhãn. Gọi trên khung trống thì không có gì thay đổi.</summary>
    public void Remove()
    {
        PalletId = null;
        AssignedSize = null;
    }
}
