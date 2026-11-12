namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

/// <summary>Các khung của một kho (theo thứ tự xếp) để chia lô.</summary>
public sealed record RoomSlots(int RoomId, string Name, IReadOnlyList<SlotCandidate> Candidates, double Margin);

/// <summary>Phần của lô vào một kho.</summary>
public sealed record RoomShare(int RoomId, string Name, IReadOnlyList<Placement> Placements);

/// <summary>Kết quả chia lô: kho nào nhận pallet nào; pallet không kho nào nhận được kèm lý do.</summary>
public sealed record DistributionResult(IReadOnlyList<RoomShare> Shares, IReadOnlyList<Rejection> Rejected);

/// <summary>
/// Chia một lô ra nhiều kho. Hàm thuần: không gọi Slot.Place, không đọc store. Khung đã chia ở lượt trước được
/// truyền cho Allocator qua "reserved" để lượt sau không lấy lại.
/// Lượt 1: type đã có sẵn trong kho nào thì vào kho đó trước (kho có nhiều pallet trùng type nhất xét trước).
/// Lượt 2: phần còn lại (type mới, hoặc kho trùng type đã đầy) vào kho xếp được nhiều nhất trước, nên kho chứa được
/// hết tự đứng đầu. Bằng nhau thì kho đang mở (preferredRoomId) trước, rồi theo RoomId: kết quả tất định.
/// </summary>
public static class Distributor
{
    public const string NoRoomFitsReason = "Không kho nào có kệ vừa pallet này.";

    public static DistributionResult Distribute(
        IReadOnlyList<Pallet> pallets, IReadOnlyList<RoomSlots> rooms, int? preferredRoomId)
    {
        ArgumentNullException.ThrowIfNull(pallets);
        ArgumentNullException.ThrowIfNull(rooms);

        var taken = rooms.ToDictionary(r => r.RoomId, _ => new Dictionary<Slot, string>());
        var placed = rooms.ToDictionary(r => r.RoomId, _ => new List<Placement>());
        var remaining = pallets.ToList();

        // Type đang có trong khung thường của kho (hàng cũ + vừa chia ở lượt trước). Nóc không tính, như Allocator.
        HashSet<string> TypesIn(RoomSlots room) => room.Candidates
            .Where(c => !c.Slot.IsTop && c.Slot.AssignedSize is not null)
            .Select(c => c.Slot.AssignedSize!)
            .Concat(taken[room.RoomId].Where(t => !t.Key.IsTop).Select(t => t.Value))
            .ToHashSet();

        void Apply(RoomSlots room, AllocationResult result)
        {
            foreach (var p in result.Placements)
            {
                taken[room.RoomId][p.Target.Slot] = p.Pallet.Size.Code;
                placed[room.RoomId].Add(p);
            }
            var done = result.Placements.Select(p => p.Pallet).ToHashSet();
            remaining = remaining.Where(p => !done.Contains(p)).ToList();
        }

        int Preferred(RoomSlots room) => room.RoomId == preferredRoomId ? 0 : 1;

        // ----- lượt 1: type trùng vào kho đã có type đó (tính lại sau mỗi kho) -----
        var pending = rooms.ToList();
        while (remaining.Count > 0)
        {
            var best = pending
                .Select(r =>
                {
                    var types = TypesIn(r);
                    return (Room: r, Match: remaining.Where(p => types.Contains(p.Size.Code)).ToList());
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

        // ----- lượt 2: phần còn lại vào kho xếp được nhiều nhất -----
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
                break;   // kho tốt nhất cũng không xếp thêm được gì thì các kho khác cũng vậy

            left.Remove(best.Room);
            Apply(best.Room, best.Result);
        }

        // Còn lại: hết chỗ hay không kho nào có kệ vừa (tính một lần cho mỗi type).
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
