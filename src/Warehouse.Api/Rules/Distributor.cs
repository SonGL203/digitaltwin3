namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

public sealed record RoomSlots(int RoomId, string Name, IReadOnlyList<SlotCandidate> Candidates, double Margin,
    IReadOnlyDictionary<Slot, StockKey>? Stock = null);

public sealed record RoomShare(int RoomId, string Name, IReadOnlyList<Placement> Placements);

public sealed record DistributionResult(IReadOnlyList<RoomShare> Shares, IReadOnlyList<Rejection> Rejected);

public static class Distributor
{
    public const string NoRoomFitsReason = "Không kho nào có kệ vừa pallet này.";

    public static DistributionResult Distribute(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<RoomSlots> rooms, int? preferredRoomId)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(rooms);

        // taken: khung đang có hàng (hàng trong kho + khung đã giữ cho lô này), kèm loại hàng.
        var taken = rooms.ToDictionary(r => r.RoomId,
            r => r.Stock is null ? new Dictionary<Slot, StockKey>() : new Dictionary<Slot, StockKey>(r.Stock));
        var placed = rooms.ToDictionary(r => r.RoomId, _ => new List<Placement>());
        var remaining = pallets.ToList();

        HashSet<string> GroupsIn(RoomSlots room) => room.Candidates
            .Where(c => !c.Slot.IsTop)
            .Select(c => taken[room.RoomId].TryGetValue(c.Slot, out var k) ? k
                       : c.Slot.AssignedSize is string s ? new StockKey(null, s) : (StockKey?)null)
            .OfType<StockKey>()
            .Select(k => k.RackGroup)
            .ToHashSet();

        void Apply(RoomSlots room, AllocationResult result)
        {
            foreach (var p in result.Placements)
            {
                taken[room.RoomId][p.Target.Slot] = StockKey.Of(p.Pallet);
                placed[room.RoomId].Add(p);
            }
            var done = result.Placements.Select(p => p.Pallet).ToHashSet();
            remaining = remaining.Where(p => !done.Contains(p)).ToList();
        }

        int Preferred(RoomSlots room) => room.RoomId == preferredRoomId ? 0 : 1;

        var pending = rooms.ToList();
        while (remaining.Count > 0)
        {
            var best = pending
                .Select(r =>
                {
                    var groups = GroupsIn(r);
                    return (Room: r, Match: remaining.Where(p => groups.Contains(StockKey.Of(p).RackGroup)).ToList());
                })
                .Where(x => x.Match.Count > 0)
                .OrderByDescending(x => x.Match.Count)
                .ThenBy(x => Preferred(x.Room))
                .ThenBy(x => x.Room.RoomId)
                .FirstOrDefault();
            if (best.Room is null)
                break;

            pending.Remove(best.Room);
            Apply(best.Room, Allocator.Allocate(best.Match, best.Room.Candidates, best.Room.Margin, taken[best.Room.RoomId]));
        }

        var left = rooms.ToList();
        while (remaining.Count > 0 && left.Count > 0)
        {
            var best = left
                .Select(r => (Room: r, Result: Allocator.Allocate(remaining, r.Candidates, r.Margin, taken[r.RoomId])))
                .OrderByDescending(x => x.Result.Placements.Count)
                .ThenBy(x => Preferred(x.Room))
                .ThenBy(x => x.Room.RoomId)
                .First();
            if (best.Result.Placements.Count == 0)
                break;

            left.Remove(best.Room);
            Apply(best.Room, best.Result);
        }

        var fitsSomewhere = remaining
            .GroupBy(p => (p.Size.Code, p.ProductTypeId))
            .ToDictionary(g => g.Key, g => rooms.Any(r => SlotFinder.Find(r.Candidates, g.First(), r.Margin).HasAnyFittingSlot));
        var rejected = remaining
            .Select(p => new Rejection(p, fitsSomewhere[(p.Size.Code, p.ProductTypeId)] ? Allocator.FullReason : NoRoomFitsReason))
            .ToList();

        var shares = rooms
            .Where(r => placed[r.RoomId].Count > 0)
            .Select(r => new RoomShare(r.RoomId, r.Name, placed[r.RoomId]))
            .ToList();
        return new DistributionResult(shares, rejected);
    }
}
