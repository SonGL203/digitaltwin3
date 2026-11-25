using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Data
{
    public interface IWarehouse3D
    {
        RoomInfoViewModel? GetRoom(int roomId);

        (int RackCount, int SlotCount) GetRoomCounts(int roomId);

        List<RackInfoViewModel> GetRacks(int roomId, ViewRect? view);

        List<SlotViewModel> GetSlots(int roomId, ViewRect? view);
    }
}
