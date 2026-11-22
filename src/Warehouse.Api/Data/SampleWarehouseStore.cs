namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

public class SampleWarehouseStore : IWarehouseStore
{
    private readonly List<Room> _rooms =
    [
        new Room
        {
            Id = 1, Name = "Kho A", Width = 20, Depth = 12, Height = 6, GridSize = 0.5, MinGap = 1, Margin = 0.05, Version = 1,
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
        },
    ];

    public Room? GetRoom(int id) => _rooms.FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<RoomSummary> GetRoomSummaries()
    {
        return _rooms.Select(r => new RoomSummary(
            r.Id, r.Name, r.Width, r.Depth, r.Height, r.GridSize,
            r.Racks.Count,
            r.Racks.Sum(k => k.Bays * (k.Levels + (k.HasTop ? 1 : 0))),
            r.Obstacles.Count)).ToList();
    }

    public bool RoomNameExists(string name)
    {
        return _rooms.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public int AddRoom(Room room)
    {
        room.Id = _rooms.Max(r => r.Id) + 1;
        _rooms.Add(room);
        return room.Id;
    }

    public IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds)
    {
        var slots = new List<Slot>();
        foreach (var rack in _rooms.SelectMany(r => r.Racks).Where(r => rackIds.Contains(r.Id)))
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
