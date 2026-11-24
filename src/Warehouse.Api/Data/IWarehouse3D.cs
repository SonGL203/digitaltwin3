using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Data
{
    public interface IWarehouse3D
    {
        RoomInfoViewModel? GetRoom(int roomId);

        List<RackInfoViewModel> GetRacks(int roomId);

        List<SlotViewModel> GetSlots(int roomId);
    }
}
