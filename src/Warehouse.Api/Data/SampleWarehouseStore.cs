namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

// BẢN TẠM: dữ liệu gõ cứng, chưa có database. Chỉ có kho số 1 với một kệ K1 và một cột.
// Dùng để chạy thử API và giao diện. Khi có PostgreSQL thì thay bằng EfWarehouseStore.
public class SampleWarehouseStore : IWarehouseStore
{
    private readonly Room _room = new()
    {
        Id = 1, Name = "Kho A", Width = 20, Depth = 12, GridSize = 0.5, MinGap = 1, Margin = 0.05, Version = 1,
        Racks =
        [
            new Rack
            {
                Id = 3, Code = "K1", X = 2, Y = 1, Rotation = 0,
                Levels = 2, LevelHeight = 1.2, Bays = 3, BayWidth = 1.3, Depth = 1.0,
                HasTop = true, MaxWeight = 1000,
            },
        ],
        Obstacles =
        [
            new Obstacle { Id = 2, Kind = ObstacleKind.Column, X = 8, Y = 6, Width = 0.4, Depth = 0.4 },
        ],
    };

    public Room? GetRoom(int id) => id == _room.Id ? _room : null;

    // Sinh khung từ cấu hình kệ: tầng 1..Levels, rồi (nếu có nóc) thêm một hàng nóc ở tầng Levels + 1
    public IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds)
    {
        var slots = new List<Slot>();
        foreach (var rack in _room.Racks.Where(r => rackIds.Contains(r.Id)))
        {
            int levelCount = rack.Levels + (rack.HasTop ? 1 : 0);
            for (int level = 1; level <= levelCount; level++)
            {
                for (int bay = 1; bay <= rack.Bays; bay++)
                {
                    bool isTop = level > rack.Levels;
                    slots.Add(new Slot
                    {
                        Id = slots.Count + 1,
                        RackId = rack.Id,
                        Level = level,
                        Bay = bay,
                        IsTop = isTop,
                        Code = isTop ? $"{rack.Code}-N-R{bay}" : $"{rack.Code}-T{level}-R{bay}",
                    });
                }
            }
        }
        return slots;
    }
}
