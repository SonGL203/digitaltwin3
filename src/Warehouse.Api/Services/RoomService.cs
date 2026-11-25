namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

public interface IRoomService
{
    IReadOnlyList<RoomSummaryViewModel> ListRooms();

    RoomViewModel? GetRoom(int roomId);

    string? CheckNewRoom(CreateRoomRequest request);

    RoomCreatedViewModel? CreateRoom(CreateRoomRequest request);
}

public class RoomService(IWarehouseStore store, ILayoutStore layoutStore) : IRoomService
{
    public IReadOnlyList<RoomSummaryViewModel> ListRooms()
    {
        return layoutStore.GetRoomSummaries()
            .Select(r => new RoomSummaryViewModel(
                r.Id, r.Name, r.Width, r.Depth, r.Height, r.GridSize,
                r.RackCount, r.SlotCount, r.ObstacleCount))
            .ToList();
    }

    public RoomViewModel? GetRoom(int roomId)
    {
        var room = store.GetRoom(roomId);
        if (room is null) return null;

        // Chỉ cần số khung mỗi kệ: đếm trên Levels, không tải cả triệu dòng Slots về rồi mới đếm
        var slotsPerRack = layoutStore.CountSlotsPerRack(roomId);

        var racks = room.Racks.Select(r =>
        {
            var bounds = r.GetBounds();
            return new RackViewModel(
                r.Id, r.Code, r.X, r.Y, r.Rotation,
                r.Levels, r.LevelHeight, r.Bays, r.BayWidth, r.Depth,
                r.HasTop, r.MaxWeight, r.ProductTypeId,
                Math.Round(bounds.Width, 6), Math.Round(bounds.Depth, 6),   // 3 × 1,2 ra 3,6 chứ không phải 3,5999…
                slotsPerRack.GetValueOrDefault(r.Id));
        }).ToList();

        var obstacles = room.Obstacles
            .Select(o => new ObstacleViewModel(o.Id, o.Kind.ToString(), o.X, o.Y, o.Width, o.Depth))
            .ToList();

        return new RoomViewModel(
            room.Id, room.Name, room.Width, room.Depth, room.Height,
            room.GridSize, room.MinGap, room.Margin, room.Version, racks, obstacles);
    }

    public string? CheckNewRoom(CreateRoomRequest request) =>
        PlacementValidator.CheckRoomValues(
            request.Name, request.Width, request.Depth, request.Height, request.GridSize, request.MinGap, request.Margin);

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
