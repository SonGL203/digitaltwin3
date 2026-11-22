namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

public interface IWarehouseStore
{
    Room? GetRoom(int id);

    IReadOnlyList<RoomSummary> GetRoomSummaries();

    bool RoomNameExists(string name);

    int AddRoom(Room room);

    IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds);
}
