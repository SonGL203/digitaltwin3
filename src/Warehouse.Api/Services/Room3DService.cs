using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Services
{
    public interface IRoom3DService
    {
        RoomInfoViewModel? GetRoom3D(int roomId);

        List<RackInfoViewModel>? GetRacks(int roomId, double? x1, double? y1, double? x2, double? y2);

        List<SlotViewModel>? GetSlots(int roomId, double? x1, double? y1, double? x2, double? y2);
    }

    public class Room3DService(IWarehouse3D warehouse3D) : IRoom3DService
    {
        public RoomInfoViewModel? GetRoom3D(int roomId)
        {
            var room = warehouse3D.GetRoom(roomId);
            if (room == null) return null;

            (room.RackCount, room.SlotCount) = warehouse3D.GetRoomCounts(roomId);
            return room;
        }

        public List<RackInfoViewModel>? GetRacks(int roomId, double? x1, double? y1, double? x2, double? y2)
        {
            if (warehouse3D.GetRoom(roomId) == null) return null;

            return warehouse3D.GetRacks(roomId, ToView(x1, y1, x2, y2));
        }

        public List<SlotViewModel>? GetSlots(int roomId, double? x1, double? y1, double? x2, double? y2)
        {
            if (warehouse3D.GetRoom(roomId) == null) return null;

            return warehouse3D.GetSlots(roomId, ToView(x1, y1, x2, y2));
        }

        private static ViewRect? ToView(double? x1, double? y1, double? x2, double? y2)
        {
            if (x1 == null || y1 == null || x2 == null || y2 == null) return null;

            return new ViewRect(
                Math.Min(x1.Value, x2.Value), Math.Min(y1.Value, y2.Value),
                Math.Max(x1.Value, x2.Value), Math.Max(y1.Value, y2.Value));
        }
    }
}
