namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

public interface IRoomService
{
    IReadOnlyList<RoomSummaryViewModel> ListRooms();

    RoomViewModel? GetRoom(int roomId);

    string? CheckNewRoom(CreateRoomRequest request);

    RoomCreatedViewModel? CreateRoom(CreateRoomRequest request);
}

public class RoomService(IWarehouseStore store) : IRoomService
{
    public IReadOnlyList<RoomSummaryViewModel> ListRooms()
    {
        return store.GetRoomSummaries()
            .Select(r => new RoomSummaryViewModel(
                r.Id, r.Name, r.Width, r.Depth, r.Height, r.GridSize,
                r.RackCount, r.SlotCount, r.ObstacleCount))
            .ToList();
    }

    public RoomViewModel? GetRoom(int roomId)
    {
        var room = store.GetRoom(roomId);
        if (room is null) return null;

        var slotsPerRack = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList())
            .GroupBy(s => s.RackId)
            .ToDictionary(g => g.Key, g => g.Count());

        var racks = room.Racks.Select(r =>
        {
            var bounds = r.GetBounds();
            return new RackViewModel(
                r.Id, r.Code, r.X, r.Y, r.Rotation,
                r.Levels, r.LevelHeight, r.Bays, r.BayWidth, r.Depth,
                r.HasTop, r.MaxWeight, r.ProductTypeId,
                bounds.Width, bounds.Depth,
                slotsPerRack.GetValueOrDefault(r.Id));
        }).ToList();

        var obstacles = room.Obstacles
            .Select(o => new ObstacleViewModel(o.Id, o.Kind.ToString(), o.X, o.Y, o.Width, o.Depth))
            .ToList();

        return new RoomViewModel(
            room.Id, room.Name, room.Width, room.Depth, room.Height,
            room.GridSize, room.MinGap, room.Margin, room.Version, racks, obstacles);
    }

    public string? CheckNewRoom(CreateRoomRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return "Chưa nhập tên kho.";
        if (name.Length > 50) return "Tên kho tối đa 50 ký tự.";
        if (request.Width < 0.5 || request.Width > 500) return "Chiều dài phải từ 0,5 đến 500 m.";
        if (request.Depth < 0.5 || request.Depth > 500) return "Chiều rộng phải từ 0,5 đến 500 m.";
        if (request.Height < 1 || request.Height > 50) return "Chiều cao trần phải từ 1 đến 50 m.";
        if (request.GridSize < 0.05 || request.GridSize > 10) return "Bước lưới phải từ 0,05 đến 10 m.";
        if (request.MinGap < 0 || request.MinGap > 20) return "Lối đi tối thiểu phải từ 0 đến 20 m.";
        if (request.Margin < 0 || request.Margin > 2) return "Khoảng hở đỉnh phải từ 0 đến 2 m.";
        return null;
    }

    public RoomCreatedViewModel? CreateRoom(CreateRoomRequest request)
    {
        var name = request.Name.Trim();
        if (store.RoomNameExists(name)) return null;

        var room = new Room
        {
            Name = name,
            Width = request.Width,
            Depth = request.Depth,
            Height = request.Height,
            GridSize = request.GridSize,
            MinGap = request.MinGap,
            Margin = request.Margin,
        };
        var id = store.AddRoom(room);
        return new RoomCreatedViewModel(id);
    }
}
