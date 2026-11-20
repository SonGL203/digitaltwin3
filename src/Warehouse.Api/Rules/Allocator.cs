namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

public sealed record Placement(Pallet Pallet, SlotCandidate Target)
{
    public bool IsTop => Target.Slot.IsTop;
}

public sealed record Rejection(Pallet Pallet, string Reason);

public sealed record AllocationResult(
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<Rejection> Rejected)
{
    public IReadOnlyList<Placement> ToTop => Placements.Where(p => p.IsTop).ToList();
}

public static class Allocator
{
    public const string NoSlotsReason = "Kho chưa có khung nào.";
    public const string FullReason = "Hết khung trống phù hợp.";

    public const int MaxPermutedTypes = 6;

    private const long MaxWork = 20_000_000;

    private sealed record Group(
        StockKey Key, IReadOnlyList<(Pallet Pallet, int Index)> Members, SlotSearchResult Search, double Volume, int FirstIndex);

    private sealed record Simulation(List<Placement> Placements, List<Rejection> Full, Score Score);

    private readonly record struct Mixing(
        int MixedProductRacks, int MixedLevels, int LevelPairs, int MixedTops, int TopPairs, int MixedRacks)
        : IComparable<Mixing>
    {
        public int CompareTo(Mixing other)
            => (MixedProductRacks, MixedLevels, LevelPairs, MixedTops, TopPairs, MixedRacks)
                .CompareTo((other.MixedProductRacks, other.MixedLevels, other.LevelPairs,
                            other.MixedTops, other.TopPairs, other.MixedRacks));

        public static Mixing Of(IEnumerable<KeyValuePair<(Rack Rack, int Level), List<Slot>>> groups,
                                IReadOnlyDictionary<Slot, StockKey> keyOfSlot)
        {
            var rows = groups
                .Select(kv => (kv.Key.Rack, IsTop: kv.Value[0].IsTop,
                               Keys: kv.Value.Where(keyOfSlot.ContainsKey).Select(s => keyOfSlot[s]).ToHashSet()))
                .ToList();
            var levels = rows.Where(r => !r.IsTop).ToList();
            var tops = rows.Where(r => r.IsTop).ToList();
            var byRack = levels.GroupBy(l => l.Rack).Select(g => g.SelectMany(l => l.Keys).ToList()).ToList();
            return new Mixing(
                byRack.Count(keys => keys.Select(k => k.ProductTypeId).Distinct().Count() > 1),
                levels.Count(l => l.Keys.Count > 1),
                levels.Sum(l => l.Keys.Count),
                tops.Count(t => t.Keys.Count > 1),
                tops.Sum(t => t.Keys.Count),
                byRack.Count(keys => keys.Distinct().Count() > 1));
        }
    }

    private readonly record struct Score(int Unplaced, int OnTop, Mixing Mix) : IComparable<Score>
    {
        public int CompareTo(Score other)
        {
            var byCount = (Unplaced, OnTop).CompareTo((other.Unplaced, other.OnTop));
            return byCount != 0 ? byCount : Mix.CompareTo(other.Mix);
        }
    }

    // held: khung đang có hàng (hàng trong kho + khung đã giữ chỗ trong kế hoạch), kèm loại hàng.
    // Không truyền thì đọc Slot.AssignedSize và coi là hàng chưa phân loại.
    public static AllocationResult Allocate(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<SlotCandidate> candidates, double margin,
        IReadOnlyDictionary<Slot, StockKey>? held = null)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(candidates);

        var existingGroups = candidates
            .Select(c => HeldBy(c.Slot, held))
            .OfType<StockKey>()
            .Select(k => k.RackGroup)
            .ToHashSet();

        var groups = pallets
            .Select((pallet, index) => (Pallet: pallet, Index: index))
            .GroupBy(x => StockKey.Of(x.Pallet))
            .Select(g =>
            {
                var members = g.ToList();
                var search = SlotFinder.Find(candidates, members[0].Pallet, margin);
                return new Group(g.Key, members, search, Volume(members[0].Pallet), members[0].Index);
            })
            .ToList();

        var rejected = groups
            .Where(g => !g.Search.HasAnyFittingSlot)
            .SelectMany(g => g.Members.Select(m => (m.Index, Rejection: new Rejection(m.Pallet,
                g.Search.RejectionReasons.Count == 0 ? NoSlotsReason : string.Join(" ", g.Search.RejectionReasons)))))
            .OrderBy(x => x.Index)
            .Select(x => x.Rejection)
            .ToList();

        var fitting = groups.Where(g => g.Search.HasAnyFittingSlot).ToList();

        Simulation? best = null;
        foreach (var order in OrdersToTry(fitting, existingGroups, pallets.Count, candidates.Count))
        {
            var run = Simulate(order, candidates, held);
            if (best is null || run.Score.CompareTo(best.Score) < 0)
                best = run;
        }

        if (best is null)
            return new AllocationResult([], rejected);

        var fits = new Dictionary<Pallet, HashSet<Slot>>();
        foreach (var group in fitting)
        {
            var slots = group.Search.FreeSlots.Select(c => c.Slot).ToHashSet();
            foreach (var (pallet, _) in group.Members)
                fits[pallet] = slots;
        }
        var (improved, full) = Improve(best.Placements, best.Full, candidates, held, fits);

        return new AllocationResult(improved, rejected.Concat(full).ToList());
    }

    private static StockKey? HeldBy(Slot slot, IReadOnlyDictionary<Slot, StockKey>? held)
        => held is not null && held.TryGetValue(slot, out var key) ? key
         : slot.AssignedSize is string size ? new StockKey(null, size) : null;

    private const int MaxImproveRounds = 10;

    private static (List<Placement> Plan, List<Rejection> Full) Improve(
        List<Placement> placements, List<Rejection> fullRejections, IReadOnlyList<SlotCandidate> candidates,
        IReadOnlyDictionary<Slot, StockKey>? held, IReadOnlyDictionary<Pallet, HashSet<Slot>> fits)
    {
        var plan = placements.ToList();
        var full = fullRejections.ToList();
        if (plan.Count == 0
            || (long)plan.Count * (plan.Count + candidates.Count + full.Count) * candidates.Count > MaxWork)
            return (plan, full);

        var keyOfSlot = new Dictionary<Slot, StockKey>();
        var rows = new Dictionary<(Rack, int), List<Slot>>();
        var slotOf = new Dictionary<Slot, SlotCandidate>();
        foreach (var c in candidates)
        {
            slotOf[c.Slot] = c;
            if (!rows.TryGetValue((c.Rack, c.Slot.Level), out var row))
                rows[(c.Rack, c.Slot.Level)] = row = new List<Slot>();
            row.Add(c.Slot);
            if (HeldBy(c.Slot, held) is StockKey stock)
                keyOfSlot[c.Slot] = stock;
        }
        foreach (var p in plan)
            keyOfSlot[p.Target.Slot] = StockKey.Of(p.Pallet);

        var score = Mixing.Of(rows, keyOfSlot);
        for (int round = 0; round < MaxImproveRounds; round++)
        {
            var changed = false;

            for (int i = 0; i < plan.Count; i++)
            {
                for (int j = i + 1; j < plan.Count; j++)
                {
                    var (a, b) = (plan[i], plan[j]);
                    var (keyA, keyB) = (StockKey.Of(a.Pallet), StockKey.Of(b.Pallet));
                    if (keyA == keyB
                        || !fits[a.Pallet].Contains(b.Target.Slot)
                        || !fits[b.Pallet].Contains(a.Target.Slot))
                        continue;

                    keyOfSlot[a.Target.Slot] = keyB;
                    keyOfSlot[b.Target.Slot] = keyA;
                    var trial = Mixing.Of(rows, keyOfSlot);
                    if (trial.CompareTo(score) < 0)
                    {
                        score = trial;
                        plan[i] = new Placement(a.Pallet, b.Target);
                        plan[j] = new Placement(b.Pallet, a.Target);
                        changed = true;
                    }
                    else
                    {
                        keyOfSlot[a.Target.Slot] = keyA;
                        keyOfSlot[b.Target.Slot] = keyB;
                    }
                }
            }

            for (int i = 0; i < plan.Count; i++)
            {
                var a = plan[i];
                var key = StockKey.Of(a.Pallet);
                foreach (var c in candidates)
                {
                    var slot = c.Slot;
                    if (slot.IsTop != a.Target.Slot.IsTop || keyOfSlot.ContainsKey(slot) || !fits[a.Pallet].Contains(slot))
                        continue;

                    keyOfSlot.Remove(a.Target.Slot);
                    keyOfSlot[slot] = key;
                    var trial = Mixing.Of(rows, keyOfSlot);
                    if (trial.CompareTo(score) < 0)
                    {
                        score = trial;
                        a = plan[i] = new Placement(a.Pallet, slotOf[slot]);
                        changed = true;
                    }
                    else
                    {
                        keyOfSlot.Remove(slot);
                        keyOfSlot[a.Target.Slot] = key;
                    }
                }
            }

            for (int i = 0; i < plan.Count; i++)
            {
                for (int k = 0; k < full.Count; k++)
                {
                    var (a, waiting) = (plan[i], full[k].Pallet);
                    var (keyA, keyW) = (StockKey.Of(a.Pallet), StockKey.Of(waiting));
                    if (keyA == keyW || !fits[waiting].Contains(a.Target.Slot))
                        continue;

                    keyOfSlot[a.Target.Slot] = keyW;
                    var trial = Mixing.Of(rows, keyOfSlot);
                    if (trial.CompareTo(score) < 0)
                    {
                        score = trial;
                        plan[i] = new Placement(waiting, a.Target);
                        full[k] = new Rejection(a.Pallet, FullReason);
                        changed = true;
                    }
                    else
                    {
                        keyOfSlot[a.Target.Slot] = keyA;
                    }
                }
            }

            if (!changed)
                break;
        }
        return (plan, full);
    }

    private static IEnumerable<IReadOnlyList<Group>> OrdersToTry(
        List<Group> groups, HashSet<string> existingGroups, int palletCount, int slotCount)
    {
        var baseOrder = groups
            .OrderBy(g => existingGroups.Contains(g.Key.RackGroup) ? 0 : 1)
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
            foreach (var permutation in Permutations(baseOrder).Skip(1))
                yield return permutation;
            yield break;
        }

        yield return baseOrder.OrderByDescending(g => g.Members.Count).ToList();
        yield return baseOrder.OrderBy(g => g.Members.Count).ToList();
        yield return baseOrder.OrderBy(g => g.Volume).ToList();
    }

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

    private static Simulation Simulate(
        IReadOnlyList<Group> ordered, IReadOnlyList<SlotCandidate> candidates, IReadOnlyDictionary<Slot, StockKey>? held)
    {
        var order = new Dictionary<Slot, int>();
        var groupsInRack = new Dictionary<Rack, HashSet<string>>();
        var keyOfSlot = new Dictionary<Slot, StockKey>();
        var slotsOfLevel = new Dictionary<(Rack, int), List<Slot>>();
        var used = new HashSet<Slot>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var (rack, slot) = (candidates[i].Rack, candidates[i].Slot);
            order[slot] = i;
            groupsInRack.TryAdd(rack, new HashSet<string>());

            if (!slotsOfLevel.TryGetValue((rack, slot.Level), out var level))
                slotsOfLevel[(rack, slot.Level)] = level = new List<Slot>();
            level.Add(slot);

            if (held is not null && held.ContainsKey(slot))
                used.Add(slot);

            if (HeldBy(slot, held) is StockKey stock)
            {
                keyOfSlot[slot] = stock;
                if (!slot.IsTop)
                    groupsInRack[rack].Add(stock.RackGroup);
            }
        }

        var placements = new List<Placement>();
        var full = new List<Rejection>();
        var leftovers = new List<(IReadOnlyList<SlotCandidate> Free, List<Pallet> Pallets)>();

        foreach (var group in ordered)
        {
            var rest = new List<Pallet>();
            foreach (var (pallet, _) in group.Members)
            {
                var target = Pick(group.Search.FreeSlots, group.Key, ownRackOnly: true);
                if (target is null)
                    rest.Add(pallet);
                else
                    Place(pallet, target);
            }
            leftovers.Add((group.Search.FreeSlots, rest));
        }

        foreach (var (free, rest) in leftovers)
        {
            foreach (var pallet in rest)
            {
                var target = Pick(free, StockKey.Of(pallet), ownRackOnly: false);
                if (target is null)
                    full.Add(new Rejection(pallet, FullReason));
                else
                    Place(pallet, target);
            }
        }

        return new Simulation(placements, full, Evaluate());

        void Place(Pallet pallet, SlotCandidate target)
        {
            var key = StockKey.Of(pallet);
            used.Add(target.Slot);
            keyOfSlot[target.Slot] = key;
            if (!target.Slot.IsTop)
                groupsInRack[target.Rack].Add(key.RackGroup);
            placements.Add(new Placement(pallet, target));
        }

        SlotCandidate? Pick(IReadOnlyList<SlotCandidate> free, StockKey key, bool ownRackOnly)
        {
            SlotCandidate? best = null;
            (bool IsTop, int Class, int LevelFit, int Adjacent, int RackPref, int Order) bestKey = default;

            foreach (var c in free)
            {
                if (used.Contains(c.Slot))
                    continue;

                var cls = Class(c, key);
                if (ownRackOnly && (cls != 0 || c.Slot.IsTop))
                    continue;

                var rank = (c.Slot.IsTop, cls, cls == 0 ? LevelFit(c, key) : 0,
                            Adjacent(c, key) ? 0 : 1, RackPref(c.Rack, key), order[c.Slot]);
                if (best is null || rank.CompareTo(bestKey) < 0)
                {
                    best = c;
                    bestKey = rank;
                }
            }
            return best;
        }

        HashSet<StockKey> KeysOnLevel(SlotCandidate c)
            => slotsOfLevel[(c.Rack, c.Slot.Level)]
                .Where(keyOfSlot.ContainsKey).Select(s => keyOfSlot[s]).ToHashSet();

        // 0 kệ của đúng loại hàng này, 1 kệ trống, 2–5 kệ đã có loại khác (xét theo tầng).
        int Class(SlotCandidate c, StockKey key)
        {
            var here = groupsInRack[c.Rack];
            if (here.Count == 0)
                return 1;
            if (here.Count == 1 && here.Contains(key.RackGroup))
                return 0;

            var onLevel = KeysOnLevel(c);
            if (onLevel.Count == 0)
                return 4;
            if (!onLevel.Contains(key))
                return 5;
            return onLevel.Count == 1 ? 2 : 3;
        }

        // Trong kệ của chính loại hàng: tầng trống hoặc tầng cùng cỡ trước,
        // rồi tầng đã lẫn có cỡ này, cuối cùng tầng của cỡ khác (cùng loại hàng).
        int LevelFit(SlotCandidate c, StockKey key)
        {
            var onLevel = KeysOnLevel(c);
            if (onLevel.Count == 0 || (onLevel.Count == 1 && onLevel.Contains(key)))
                return 0;
            return onLevel.Contains(key) ? 1 : 2;
        }

        bool Adjacent(SlotCandidate c, StockKey key)
            => slotsOfLevel[(c.Rack, c.Slot.Level)]
                .Any(s => Math.Abs(s.Bay - c.Slot.Bay) == 1 && keyOfSlot.TryGetValue(s, out var t) && t == key);

        int RackPref(Rack rack, StockKey key)
        {
            var here = groupsInRack[rack];
            return here.Contains(key.RackGroup) ? 0 : here.Count >= 2 ? 1 : 2;
        }

        Score Evaluate()
            => new(full.Count, placements.Count(p => p.IsTop), Mixing.Of(slotsOfLevel, keyOfSlot));
    }

    private static double Volume(Pallet pallet)
        => pallet.Size.Length * pallet.Size.Width * pallet.Size.Height;
}
