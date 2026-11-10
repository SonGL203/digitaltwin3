namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

/// <summary>Pallet được xếp vào khung nào. IsTop là khung nóc (pallet quá cao so với mọi tầng).</summary>
public sealed record Placement(Pallet Pallet, SlotCandidate Target)
{
    public bool IsTop => Target.Slot.IsTop;
}

/// <summary>Pallet không xếp được, kèm lý do.</summary>
public sealed record Rejection(Pallet Pallet, string Reason);

/// <summary>
/// Kết quả xếp một lô. Placements theo thứ tự đã xếp (pallet ít lựa chọn trước), không theo thứ tự đến.
/// </summary>
public sealed record AllocationResult(
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<Rejection> Rejected)
{
    public IReadOnlyList<Placement> ToTop => Placements.Where(p => p.IsTop).ToList();
}

/// <summary>
/// Xếp cả một lô pallet vào các khung. Hàm thuần: không gọi Slot.Place, không sửa khung, không đọc store.
/// PalletService mới là nơi Place() theo Placements rồi lưu.
/// </summary>
public static class Allocator
{
    public const string NoSlotsReason = "Kho chưa có khung nào.";
    public const string FullReason = "Hết khung trống phù hợp.";

    /// <summary>
    /// 1) Pallet không khung nào vừa → Rejected ngay, kèm lý do từ SlotFinder.
    /// 2) Các pallet còn lại xếp theo thứ tự: ÍT khung trống nhận được nhất trước (bằng nhau thì thể tích lớn trước,
    ///    rồi theo thứ tự đầu vào) vì pallet kén chỗ mà xếp sau thì dễ mất chỗ vào tay pallet dễ tính.
    /// 3) Mỗi pallet lấy khung trống đầu tiên (theo thứ tự candidates: kệ → tầng → ô, nóc cuối) chưa bị pallet khác
    ///    trong lượt này chiếm; không còn thì Rejected "hết khung trống phù hợp".
    /// Cùng dữ liệu vào cho cùng kết quả (không dùng số ngẫu nhiên).
    /// </summary>
    public static AllocationResult Allocate(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<SlotCandidate> candidates, double margin)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(candidates);

        var rejected = new List<Rejection>();
        var queue = new List<(Pallet Pallet, int Options, int Index)>();

        for (int i = 0; i < pallets.Count; i++)
        {
            var search = SlotFinder.Find(candidates, pallets[i], margin);

            if (!search.HasAnyFittingSlot)
            {
                string reason = search.RejectionReasons.Count == 0
                    ? NoSlotsReason
                    : string.Join(" ", search.RejectionReasons);
                rejected.Add(new Rejection(pallets[i], reason));
            }
            else
            {
                queue.Add((pallets[i], search.FreeSlots.Count, i));
            }
        }

        var ordered = queue
            .OrderBy(x => x.Options)
            .ThenByDescending(x => Volume(x.Pallet))
            .ThenBy(x => x.Index);

        var placements = new List<Placement>();
        var used = new HashSet<Slot>();

        foreach (var item in ordered)
        {
            var search = SlotFinder.Find(candidates, item.Pallet, margin);
            SlotCandidate? target = search.FreeSlots.FirstOrDefault(c => !used.Contains(c.Slot));

            if (target is null)
            {
                rejected.Add(new Rejection(item.Pallet, FullReason));
            }
            else
            {
                used.Add(target.Slot);
                placements.Add(new Placement(item.Pallet, target));
            }
        }

        return new AllocationResult(placements, rejected);
    }

    private static double Volume(Pallet pallet)
        => pallet.Size.Length * pallet.Size.Width * pallet.Size.Height;
}
