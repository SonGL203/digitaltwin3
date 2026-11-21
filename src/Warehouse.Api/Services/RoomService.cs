namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.ViewModels;

public interface IRoomService
{
    RoomViewModel? GetRoom(int roomId);    // null = không có kho này
}

public class RoomService(IWarehouseStore store) : IRoomService
{
    // Lấy một kho kèm kệ, tường/cột để giao diện vẽ.
    // Service: lấy dữ liệu từ store rồi đổi sang ViewModel. Không biết gì về HTTP (mã 200, 404 là việc của Controller).
    public RoomViewModel? GetRoom(int roomId)
    {
        // 1. Lấy từ store
        var room = store.GetRoom(roomId);
        if (room is null) return null;

        // 2. Đếm số khung của từng kệ
        var slotsPerRack = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList())
            .GroupBy(s => s.RackId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 3. Đổi từng kệ, từng tường/cột sang ViewModel
        var racks = room.Racks.Select(r =>
        {
            var bounds = r.GetBounds();   // chỗ kệ chiếm trên sàn, đã tính xoay 90° (luật nằm trong Rack)
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

        // 4. Trả kết quả
        return new RoomViewModel(
            room.Id, room.Name, room.Width, room.Depth, room.GridSize,
            room.MinGap, room.Margin, room.Version, racks, obstacles);
    }
}
