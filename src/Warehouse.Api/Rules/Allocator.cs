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
/// Kết quả xếp một lô. Placements theo thứ tự đã xếp (từng type một, type kén chỗ nhất trước), không theo thứ tự đến.
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
    /// 1) Gom pallet theo TYPE (cùng mã type và cùng loại hàng). Type nào không khung nào vừa → Rejected ngay,
    ///    kèm lý do từ SlotFinder.
    /// 2) Xếp từng type một, trọn type này rồi mới sang type khác. Type có ÍT khung nhận được nhất xếp trước
    ///    (bằng nhau thì thể tích lớn trước, rồi thứ tự đầu vào) vì type kén chỗ mà xếp sau thì dễ mất chỗ.
    /// 3) Mỗi pallet chọn khung trống vừa theo thứ tự ưu tiên (sau khi để khung nóc xuống cuối cùng):
    ///      0. kệ đã có pallet CÙNG type (dồn một type vào một chỗ);
    ///      1. kệ chưa có pallet nào (mỗi type một kệ riêng, dù kệ trước còn chỗ trống);
    ///      2. một tầng (ngăn) còn TRỐNG NGUYÊN trong kệ đã có hàng khác;
    ///      3. bất kỳ khung trống còn lại (phần dư nhét vào chỗ trống khi hết kệ riêng).
    ///    Bằng hạng thì lấy khung đứng trước theo thứ tự candidates (kệ → tầng → ô). Nóc là chỗ cuối cùng: khung nóc của
    ///    bất kỳ kệ nào chỉ được dùng khi không còn khung thường nào vừa.
    /// Không dùng số ngẫu nhiên nên cùng dữ liệu vào cho cùng kết quả. Hết khung → Rejected "Hết khung trống phù hợp".
    /// </summary>
    public static AllocationResult Allocate(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<SlotCandidate> candidates, double margin)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(candidates);

        // Trạng thái kho trong lượt này (chỉ trong bộ nhớ, không sửa khung thật): kệ nào đang giữ type nào,
        // kệ nào đã có hàng, khung nào đã bị chiếm, và các khung của từng tầng để biết tầng nào còn trống nguyên.
        var typesInRack = new Dictionary<Rack, HashSet<string>>();
        var rackHasPallet = new HashSet<Rack>();
        var used = new HashSet<Slot>();
        var slotsOfLevel = new Dictionary<(Rack, int), List<Slot>>();
        var order = new Dictionary<Slot, int>();   // thứ tự gốc của khung trong candidates, để phá hoà

        for (int i = 0; i < candidates.Count; i++)
        {
            var (rack, slot) = (candidates[i].Rack, candidates[i].Slot);
            order[slot] = i;

            if (!typesInRack.ContainsKey(rack))
                typesInRack[rack] = new HashSet<string>();

            if (!slotsOfLevel.TryGetValue((rack, slot.Level), out var level))
                slotsOfLevel[(rack, slot.Level)] = level = new List<Slot>();
            level.Add(slot);

            if (!slot.IsEmpty)
            {
                rackHasPallet.Add(rack);
                if (slot.AssignedSize is not null)
                    typesInRack[rack].Add(slot.AssignedSize);
            }
        }

        // ----- gom theo type -----
        var groups = pallets
            .Select((pallet, index) => (Pallet: pallet, Index: index))
            .GroupBy(x => (x.Pallet.Size.Code, x.Pallet.ProductTypeId))
            .Select(g =>
            {
                var members = g.ToList();
                var search = SlotFinder.Find(candidates, members[0].Pallet, margin);   // cùng type nên cùng kết quả
                return (Members: members, Search: search, Volume: Volume(members[0].Pallet));
            })
            .ToList();

        // Pallet không khung nào vừa: từ chối ngay, giữ thứ tự đầu vào.
        var rejected = groups
            .Where(g => !g.Search.HasAnyFittingSlot)
            .SelectMany(g => g.Members.Select(m => (m.Index, Rejection: new Rejection(m.Pallet,
                g.Search.RejectionReasons.Count == 0 ? NoSlotsReason : string.Join(" ", g.Search.RejectionReasons)))))
            .OrderBy(x => x.Index)
            .Select(x => x.Rejection)
            .ToList();

        var ordered = groups
            .Where(g => g.Search.HasAnyFittingSlot)
            .OrderBy(g => g.Search.FreeSlots.Count)
            .ThenByDescending(g => g.Volume)
            .ThenBy(g => g.Members[0].Index);

        var placements = new List<Placement>();

        foreach (var group in ordered)
        {
            foreach (var (pallet, _) in group.Members)
            {
                var typeCode = pallet.Size.Code;

                SlotCandidate? best = null;
                (bool IsTop, int Rank, int Order) bestKey = default;

                foreach (var candidate in group.Search.FreeSlots)
                {
                    if (used.Contains(candidate.Slot))
                        continue;

                    var key = (candidate.Slot.IsTop, Rank(candidate), order[candidate.Slot]);
                    if (best is null || key.CompareTo(bestKey) < 0)
                    {
                        best = candidate;
                        bestKey = key;
                    }
                }

                if (best is null)
                {
                    rejected.Add(new Rejection(pallet, FullReason));
                    continue;
                }

                used.Add(best.Slot);
                rackHasPallet.Add(best.Rack);
                typesInRack[best.Rack].Add(typeCode);
                placements.Add(new Placement(pallet, best));

                int Rank(SlotCandidate c)
                {
                    if (typesInRack[c.Rack].Contains(typeCode))
                        return 0;
                    if (!rackHasPallet.Contains(c.Rack))
                        return 1;
                    if (slotsOfLevel[(c.Rack, c.Slot.Level)].All(s => s.IsEmpty && !used.Contains(s)))
                        return 2;
                    return 3;
                }
            }
        }

        return new AllocationResult(placements, rejected);
    }

    private static double Volume(Pallet pallet)
        => pallet.Size.Length * pallet.Size.Width * pallet.Size.Height;
}
