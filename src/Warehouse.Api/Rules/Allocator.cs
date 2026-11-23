namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

public record FreeSlot(int Id, string Code, string RackCode, bool IsTop);

public record PalletGroup(PalletType Type, int? ProductTypeId, List<Pallet> Pallets);

public record Placement(Pallet Pallet, FreeSlot Slot);

public record Rejection(Pallet Pallet, string Reason);

public record AllocationResult(List<Placement> Placements, List<Rejection> Rejected);

public static class Allocator
{
    public const string NoFitReason = "Không có khung vừa với pallet này.";
    public const string FullReason = "Hết khung trống phù hợp.";

    public static List<PalletGroup> GroupPallets(IReadOnlyList<Pallet> pallets)
    {
        var byKey = new Dictionary<(int TypeId, int? ProductTypeId), PalletGroup>();
        var groups = new List<PalletGroup>();

        foreach (Pallet p in pallets)
        {
            var key = (p.Type.Id, p.ProductTypeId);

            if (!byKey.TryGetValue(key, out PalletGroup? group))
            {
                group = new PalletGroup(p.Type, p.ProductTypeId, new List<Pallet>());
                byKey[key] = group;
                groups.Add(group);
            }

            group.Pallets.Add(p);
        }

        return groups;
    }

    public static AllocationResult Assign(IReadOnlyList<(PalletGroup Group, List<FreeSlot> FreeSlots)> groups)
    {
        var placements = new List<Placement>();
        var rejected = new List<Rejection>();
        var used = new HashSet<int>();

        var ordered = new List<(PalletGroup Group, List<FreeSlot> FreeSlots)>(groups);
        ordered.Sort(CompareGroups);

        foreach (var (group, freeSlots) in ordered)
        {
            if (freeSlots.Count == 0)
            {
                foreach (var pallet in group.Pallets)
                    rejected.Add(new Rejection(pallet, NoFitReason));
                continue;
            }

            int next = 0;
            foreach (var pallet in group.Pallets)
            {
                while (next < freeSlots.Count && used.Contains(freeSlots[next].Id))
                    next++;

                if (next == freeSlots.Count)
                {
                    rejected.Add(new Rejection(pallet, FullReason));
                    continue;
                }

                var slot = freeSlots[next];
                used.Add(slot.Id);
                placements.Add(new Placement(pallet, slot));
                next++;
            }
        }

        return new AllocationResult(placements, rejected);
    }

    private static int CompareGroups((PalletGroup Group, List<FreeSlot> FreeSlots) a,
                                     (PalletGroup Group, List<FreeSlot> FreeSlots) b)
    {
        int bySlots = a.FreeSlots.Count.CompareTo(b.FreeSlots.Count);
        if (bySlots != 0) return bySlots;

        int byVolume = Volume(b.Group.Type).CompareTo(Volume(a.Group.Type));
        if (byVolume != 0) return byVolume;

        return a.Group.Pallets[0].Id.CompareTo(b.Group.Pallets[0].Id);
    }

    private static double Volume(PalletType type)
    {
        return type.Length * type.Width * type.Height;
    }
}
