using Warehouse.Api.Data;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Services
{
    public interface IRoom3DService
    {
        RoomInfoViewModel? GetRoom3D(int roomId);

        List<SlotViewModel>? GetSlots(int roomId);
    }

    public class Room3DService(IWarehouse3D warehouse3D) : IRoom3DService
    {
        public RoomInfoViewModel? GetRoom3D(int roomId)
        {
            var room = warehouse3D.GetRoom(roomId);
            if (room == null) return null;

            room.Racks = warehouse3D.GetRacks(roomId);
            return room;
        }

        public List<SlotViewModel>? GetSlots(int roomId)
        {
            if (warehouse3D.GetRoom(roomId) == null) return null;

            return warehouse3D.GetSlots(roomId);
        }
    }
}
