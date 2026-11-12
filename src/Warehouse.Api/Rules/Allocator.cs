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
/// Kết quả xếp một lô. Placements theo thứ tự đã xếp (bước 1 rồi bước 2, từng type một), không theo thứ tự đến.
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

    /// <summary>Lô có tối đa chừng này type thì thử MỌI thứ tự xếp type (6! = 720 lần mô phỏng).</summary>
    public const int MaxPermutedTypes = 6;

    /// <summary>Giới hạn khối lượng mô phỏng (số lần × pallet × khung) để một request không treo máy.</summary>
    private const long MaxWork = 20_000_000;

    private sealed record Group(
        string Code, IReadOnlyList<(Pallet Pallet, int Index)> Members, SlotSearchResult Search, double Volume, int FirstIndex);

    private sealed record Simulation(List<Placement> Placements, List<Rejection> Full, Score Score);

    /// <summary>
    /// Điểm của một kết quả xếp: nhỏ hơn là tốt hơn, so lần lượt: ít pallet bị từ chối → ít pallet lên nóc
    /// → ít tầng lẫn → ít cặp (tầng, type) → ít kệ lẫn.
    /// </summary>
    private readonly record struct Score(int Unplaced, int OnTop, int MixedLevels, int LevelTypePairs, int MixedRacks)
        : IComparable<Score>
    {
        public int CompareTo(Score other)
            => (Unplaced, OnTop, MixedLevels, LevelTypePairs, MixedRacks)
                .CompareTo((other.Unplaced, other.OnTop, other.MixedLevels, other.LevelTypePairs, other.MixedRacks));
    }

    /// <summary>
    /// DUYỆT TRƯỚC rồi mới chọn: mô phỏng việc xếp với nhiều thứ tự type khác nhau (chỉ trong bộ nhớ), chấm điểm
    /// trạng thái cuối của từng lần, trả kết quả điểm tốt nhất (bằng điểm thì lấy lần thử trước).
    /// Lần thử đầu luôn là thứ tự mặc định (type đã có trong kho → ít khung nhận được → to trước → thứ tự nhập) nên
    /// kết quả không bao giờ kém thứ tự đó. Lô ≤ 6 type: thử mọi thứ tự; nhiều hơn hoặc lô quá lớn: vài thứ tự tiêu biểu.
    ///
    /// Mỗi lần mô phỏng:
    /// 1) Bước 1 (giữ chỗ cho chủ kệ): mọi type lấp KỆ RIÊNG của nó trước (kệ mà khung thường chỉ chứa type đó).
    /// 2) Bước 2 (phần dư), từng type một theo thứ tự đang thử, mỗi pallet chọn khung theo khoá
    ///    (nóc?, hạng, sát bên?, kệ, thứ tự khung): hạng 0 kệ chỉ chứa type này; 1 kệ trống hẳn; 2 tầng chỉ có type này;
    ///    3 tầng đã lẫn có type này; 4 tầng trống nguyên; 5 tầng chỉ có type khác.
    /// "Type của kệ/tầng" chỉ tính khung thường (nóc không tính). reserved: khung đã được giữ ở lượt trước (Distributor),
    /// coi như đã có hàng type đó. Các khoá chỉ đổi THỨ TỰ, không loại khung nào, nên pallet chỉ bị từ chối khi thật sự
    /// hết khung vừa. Không dùng số ngẫu nhiên: cùng dữ liệu vào cho cùng kết quả.
    /// </summary>
    public static AllocationResult Allocate(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<SlotCandidate> candidates, double margin,
        IReadOnlyDictionary<Slot, string>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(candidates);

        var existingTypes = candidates
            .Select(c => reserved is not null && reserved.TryGetValue(c.Slot, out var t) ? t : c.Slot.AssignedSize)
            .OfType<string>()
            .ToHashSet();

        // ----- gom theo type -----
        var groups = pallets
            .Select((pallet, index) => (Pallet: pallet, Index: index))
            .GroupBy(x => (x.Pallet.Size.Code, x.Pallet.ProductTypeId))
            .Select(g =>
            {
                var members = g.ToList();
                var search = SlotFinder.Find(candidates, members[0].Pallet, margin);   // cùng type nên cùng kết quả
                return new Group(g.Key.Code, members, search, Volume(members[0].Pallet), members[0].Index);
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

        var fitting = groups.Where(g => g.Search.HasAnyFittingSlot).ToList();

        // ----- duyệt trước: mô phỏng từng thứ tự, giữ lần điểm tốt nhất -----
        Simulation? best = null;
        foreach (var order in OrdersToTry(fitting, existingTypes, pallets.Count, candidates.Count))
        {
            var run = Simulate(order, candidates, reserved);
            if (best is null || run.Score.CompareTo(best.Score) < 0)
                best = run;
        }

        if (best is null)
            return new AllocationResult([], rejected);

        // ----- cải thiện cục bộ: đổi chỗ từng cặp pallet khác type trong kế hoạch tốt nhất -----
        var fits = new Dictionary<Pallet, HashSet<Slot>>();
        foreach (var group in fitting)
        {
            var slots = group.Search.FreeSlots.Select(c => c.Slot).ToHashSet();
            foreach (var (pallet, _) in group.Members)
                fits[pallet] = slots;
        }
        var improved = Improve(best.Placements, candidates, reserved, fits);

        return new AllocationResult(improved, rejected.Concat(best.Full).ToList());
    }

    /// <summary>Số vòng đổi chỗ tối đa (mỗi vòng thử mọi cặp; dừng sớm khi một vòng không cải thiện gì).</summary>
    private const int MaxImproveRounds = 10;

    /// <summary>
    /// Duyệt các thứ tự type chưa đủ: trong một type, phần dư vẫn chọn ô tham lam từng pallet, nên có thể ra một tầng
    /// 3 type (ví dụ lô b46: B B A D) trong khi đổi cái A với cái D trên nóc thì tầng đó chỉ còn B, D.
    /// Bước này thử ĐỔI CHỖ từng cặp pallet khác type trong kế hoạch (chỉ khi cả hai vừa ô của nhau), giữ lần đổi làm
    /// điểm "lẫn" tốt hơn (ít tầng lẫn → ít cặp tầng-type → ít kệ lẫn), lặp tới khi không còn lần đổi nào tốt hơn.
    /// Đổi chỗ giữ nguyên số pallet đã xếp và số pallet trên nóc (hai pallet đổi khung cho nhau), nên không làm hỏng
    /// hai tiêu chí đầu của điểm; và chỉ giữ khi tốt hơn nên kết quả không bao giờ kém kế hoạch ban đầu.
    /// </summary>
    private static List<Placement> Improve(
        List<Placement> placements, IReadOnlyList<SlotCandidate> candidates,
        IReadOnlyDictionary<Slot, string>? reserved, IReadOnlyDictionary<Pallet, HashSet<Slot>> fits)
    {
        var plan = placements.ToList();
        if (plan.Count < 2 || (long)plan.Count * plan.Count * candidates.Count > MaxWork)
            return plan;   // lô quá lớn: bỏ qua bước này để request không treo máy

        var typeOfSlot = new Dictionary<Slot, string>();
        var levels = new Dictionary<(Rack, int), List<Slot>>();
        foreach (var c in candidates)
        {
            if (!c.Slot.IsTop)
            {
                if (!levels.TryGetValue((c.Rack, c.Slot.Level), out var level))
                    levels[(c.Rack, c.Slot.Level)] = level = new List<Slot>();
                level.Add(c.Slot);
            }
            var held = reserved is not null && reserved.TryGetValue(c.Slot, out var r) ? r : c.Slot.AssignedSize;
            if (held is not null)
                typeOfSlot[c.Slot] = held;
        }
        foreach (var p in plan)
            typeOfSlot[p.Target.Slot] = p.Pallet.Size.Code;

        var score = Mixing();
        for (int round = 0; round < MaxImproveRounds; round++)
        {
            var changed = false;
            for (int i = 0; i < plan.Count; i++)
            {
                for (int j = i + 1; j < plan.Count; j++)
                {
                    var (a, b) = (plan[i], plan[j]);
                    var (typeA, typeB) = (a.Pallet.Size.Code, b.Pallet.Size.Code);
                    if (typeA == typeB
                        || !fits[a.Pallet].Contains(b.Target.Slot)
                        || !fits[b.Pallet].Contains(a.Target.Slot))
                        continue;

                    typeOfSlot[a.Target.Slot] = typeB;
                    typeOfSlot[b.Target.Slot] = typeA;
                    var trial = Mixing();
                    if (trial.CompareTo(score) < 0)
                    {
                        score = trial;
                        plan[i] = new Placement(a.Pallet, b.Target);   // giữ thứ tự xếp, chỉ đổi khung
                        plan[j] = new Placement(b.Pallet, a.Target);
                        changed = true;
                    }
                    else
                    {
                        typeOfSlot[a.Target.Slot] = typeA;              // không tốt hơn: trả lại như cũ
                        typeOfSlot[b.Target.Slot] = typeB;
                    }
                }
            }
            if (!changed)
                break;
        }
        return plan;

        // Điểm "lẫn" của trạng thái hiện tại (nhỏ hơn là tốt hơn): tầng lẫn, cặp (tầng, type), kệ lẫn. Nóc không tính.
        (int MixedLevels, int Pairs, int MixedRacks) Mixing()
        {
            var types = levels.Select(kv => (Rack: kv.Key.Item1,
                Set: kv.Value.Where(typeOfSlot.ContainsKey).Select(s => typeOfSlot[s]).ToHashSet())).ToList();
            return (types.Count(t => t.Set.Count > 1),
                    types.Sum(t => t.Set.Count),
                    types.GroupBy(t => t.Rack).Count(g => g.SelectMany(t => t.Set).Distinct().Count() > 1));
        }
    }

    /// <summary>
    /// Các thứ tự type sẽ thử, theo thứ tự cố định (để tất định). Đầu tiên luôn là thứ tự mặc định.
    /// Đủ ngân sách thì thử mọi hoán vị; không thì thêm vài thứ tự tiêu biểu.
    /// </summary>
    private static IEnumerable<IReadOnlyList<Group>> OrdersToTry(
        List<Group> groups, HashSet<string> existingTypes, int palletCount, int slotCount)
    {
        var baseOrder = groups
            .OrderBy(g => existingTypes.Contains(g.Code) ? 0 : 1)
            .ThenBy(g => g.Search.FreeSlots.Count)
            .ThenByDescending(g => g.Volume)
            .ThenBy(g => g.FirstIndex)
            .ToList();
        yield return baseOrder;
        if (baseOrder.Count < 2)
            yield break;

        var budget = MaxWork / Math.Max(1L, (long)palletCount * slotCount);
        if (baseOrder.Count <= MaxPermutedTypes && Factorial(baseOrder.Count) <= budget)
        {
            foreach (var permutation in Permutations(baseOrder).Skip(1))   // phần tử đầu chính là baseOrder
                yield return permutation;
            yield break;
        }

        // Quá nhiều type hoặc lô quá lớn: vài thứ tự tiêu biểu (sắp ổn định, nên hoà vẫn theo thứ tự mặc định).
        yield return baseOrder.OrderByDescending(g => g.Members.Count).ToList();
        yield return baseOrder.OrderBy(g => g.Members.Count).ToList();
        yield return baseOrder.OrderBy(g => g.Volume).ToList();
    }

    /// <summary>Mọi hoán vị theo thứ tự từ điển của chỉ số (hoán vị đầu tiên là danh sách gốc).</summary>
    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        var used = new bool[items.Count];
        var current = new List<T>(items.Count);

        IEnumerable<IReadOnlyList<T>> Next()
        {
            if (current.Count == items.Count)
            {
                yield return current.ToList();
                yield break;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                current.Add(items[i]);
                foreach (var permutation in Next())
                    yield return permutation;
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }

        return Next();
    }

    private static long Factorial(int n)
    {
        long result = 1;
        for (int i = 2; i <= n; i++)
            result *= i;
        return result;
    }

    /// <summary>
    /// Một lần xếp thử với một thứ tự type: bước 1 rồi bước 2, trạng thái riêng (không đụng khung thật).
    /// Trả các vị trí đã chọn, pallet hết chỗ và điểm của trạng thái cuối.
    /// </summary>
    private static Simulation Simulate(
        IReadOnlyList<Group> ordered, IReadOnlyList<SlotCandidate> candidates, IReadOnlyDictionary<Slot, string>? reserved)
    {
        var order = new Dictionary<Slot, int>();                        // thứ tự gốc của khung, để phá hoà
        var typesInRack = new Dictionary<Rack, HashSet<string>>();      // type trong KHUNG THƯỜNG của kệ (không tính nóc)
        var typeOfSlot = new Dictionary<Slot, string>();                // khung nào đang giữ type nào (hàng cũ + vừa xếp)
        var slotsOfLevel = new Dictionary<(Rack, int), List<Slot>>();   // các khung cùng tầng
        var used = new HashSet<Slot>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var (rack, slot) = (candidates[i].Rack, candidates[i].Slot);
            order[slot] = i;
            typesInRack.TryAdd(rack, new HashSet<string>());

            if (!slotsOfLevel.TryGetValue((rack, slot.Level), out var level))
                slotsOfLevel[(rack, slot.Level)] = level = new List<Slot>();
            level.Add(slot);

            var held = slot.AssignedSize;
            if (reserved is not null && reserved.TryGetValue(slot, out var reservedType))
            {
                held = reservedType;
                used.Add(slot);
            }

            if (held is not null)
            {
                typeOfSlot[slot] = held;
                if (!slot.IsTop)
                    typesInRack[rack].Add(held);   // nóc không làm kệ thành "lẫn"
            }
        }

        var placements = new List<Placement>();
        var full = new List<Rejection>();
        var leftovers = new List<(IReadOnlyList<SlotCandidate> Free, List<Pallet> Pallets)>();

        // ----- bước 1: mỗi type lấp kệ riêng của nó trước (giữ chỗ cho chủ kệ) -----
        foreach (var group in ordered)
        {
            var rest = new List<Pallet>();
            foreach (var (pallet, _) in group.Members)
            {
                var target = Pick(group.Search.FreeSlots, pallet.Size.Code, ownRackOnly: true);
                if (target is null)
                    rest.Add(pallet);
                else
                    Place(pallet, target);
            }
            leftovers.Add((group.Search.FreeSlots, rest));
        }

        // ----- bước 2: phần dư, từng type một, theo hạng -----
        foreach (var (free, rest) in leftovers)
        {
            foreach (var pallet in rest)
            {
                var target = Pick(free, pallet.Size.Code, ownRackOnly: false);
                if (target is null)
                    full.Add(new Rejection(pallet, FullReason));
                else
                    Place(pallet, target);
            }
        }

        return new Simulation(placements, full, Evaluate());

        // Chỗ duy nhất cập nhật trạng thái: không thể quên một trong ba.
        void Place(Pallet pallet, SlotCandidate target)
        {
            used.Add(target.Slot);
            typeOfSlot[target.Slot] = pallet.Size.Code;
            if (!target.Slot.IsTop)
                typesInRack[target.Rack].Add(pallet.Size.Code);
            placements.Add(new Placement(pallet, target));
        }

        // Chọn khung có khoá nhỏ nhất. ownRackOnly = bước 1: chỉ khung thường trong kệ riêng (hạng 0).
        SlotCandidate? Pick(IReadOnlyList<SlotCandidate> free, string type, bool ownRackOnly)
        {
            SlotCandidate? best = null;
            (bool IsTop, int Class, int Adjacent, int RackPref, int Order) bestKey = default;

            foreach (var c in free)
            {
                if (used.Contains(c.Slot))
                    continue;

                var cls = Class(c, type);
                if (ownRackOnly && (cls != 0 || c.Slot.IsTop))
                    continue;

                var key = (c.Slot.IsTop, cls, Adjacent(c, type) ? 0 : 1, RackPref(c.Rack, type), order[c.Slot]);
                if (best is null || key.CompareTo(bestKey) < 0)
                {
                    best = c;
                    bestKey = key;
                }
            }
            return best;
        }

        // Hạng của khung với type này: 0 kệ riêng, 1 kệ trống hẳn, rồi xét theo TẦNG (đơn vị tính "lẫn").
        int Class(SlotCandidate c, string type)
        {
            var here = typesInRack[c.Rack];
            if (here.Count == 0)
                return 1;                                   // kệ trống hẳn
            if (here.Count == 1 && here.Contains(type))
                return 0;                                   // kệ riêng

            var onLevel = slotsOfLevel[(c.Rack, c.Slot.Level)]
                .Where(typeOfSlot.ContainsKey).Select(s => typeOfSlot[s]).ToHashSet();
            if (onLevel.Count == 0)
                return 4;                                   // tầng trống nguyên
            if (!onLevel.Contains(type))
                return 5;                                   // tầng của type khác: tạo thêm tầng lẫn, sau cùng
            return onLevel.Count == 1 ? 2 : 3;              // tầng chỉ có type này / tầng đã lẫn có type này
        }

        // Ô sát bên (cùng tầng, ô liền trước hoặc liền sau) là type này.
        bool Adjacent(SlotCandidate c, string type)
            => slotsOfLevel[(c.Rack, c.Slot.Level)]
                .Any(s => Math.Abs(s.Bay - c.Slot.Bay) == 1 && typeOfSlot.TryGetValue(s, out var t) && t == type);

        // Cùng hạng: kệ đã có type này → kệ đã lẫn → kệ sạch của type khác.
        int RackPref(Rack rack, string type)
        {
            var here = typesInRack[rack];
            return here.Contains(type) ? 0 : here.Count >= 2 ? 1 : 2;
        }

        // Chấm điểm trạng thái CUỐI (hàng cũ + hàng vừa xếp). Tầng và kệ chỉ tính khung thường.
        Score Evaluate()
        {
            var levels = slotsOfLevel
                .Where(kv => !kv.Value[0].IsTop)
                .Select(kv => (Rack: kv.Key.Item1,
                               Types: kv.Value.Where(typeOfSlot.ContainsKey).Select(s => typeOfSlot[s]).ToHashSet()))
                .ToList();
            var mixedRacks = levels
                .GroupBy(l => l.Rack)
                .Count(g => g.SelectMany(l => l.Types).Distinct().Count() > 1);
            return new Score(
                full.Count,
                placements.Count(p => p.IsTop),
                levels.Count(l => l.Types.Count > 1),
                levels.Sum(l => l.Types.Count),
                mixedRacks);
        }
    }

    private static double Volume(Pallet pallet)
        => pallet.Size.Length * pallet.Size.Width * pallet.Size.Height;
}
