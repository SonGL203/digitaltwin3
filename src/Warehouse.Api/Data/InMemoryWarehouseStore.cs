namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

/// <summary>
/// Kho lưu trong RAM: dữ liệu mất khi tắt chương trình. Dùng làm store tạm cho ngày 1–3 và làm kho giả trong test;
/// EfWarehouseStore (ngày 4) sẽ thay thế mà các tầng trên không phải sửa.
/// Khác cơ sở dữ liệu thật ở một điểm: trả về chính các đối tượng đang giữ (không sao chép), nên sửa một khung
/// (ví dụ Place) là đổi luôn dữ liệu đã lưu. Đừng dựa vào điều này; luôn gọi Save/Add khi cần lưu.
/// </summary>
public class InMemoryWarehouseStore : IWarehouseStore
{
    private readonly object _lock = new();
    private readonly List<Slot> _slots = new();
    private Room? _room;
    private int _nextRoomId = 1;
    private int _nextRackId = 1;
    private int _nextSlotId = 1;

    public Room? GetRoom()
    {
        lock (_lock)
            return _room;
    }

    public void SaveRoom(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (_lock)
        {
            if (room.Id == 0)
                SetId(room, _nextRoomId++);

            foreach (var rack in room.Racks.Where(r => r.Id == 0))
                SetId(rack, _nextRackId++);

            _room = room;
        }
    }

    public IReadOnlyList<Slot> GetAllSlots()
    {
        lock (_lock)
            return _slots.ToList();
    }

    public IReadOnlyList<Slot> GetSlotsOfRack(int rackId)
    {
        lock (_lock)
            return _slots.Where(s => s.RackId == rackId).ToList();
    }

    public void AddSlots(IEnumerable<Slot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        lock (_lock)
        {
            foreach (var slot in slots)
            {
                if (slot.Id == 0)
                    SetId(slot, _nextSlotId++);

                _slots.Add(slot);
            }
        }
    }

    public void RemoveSlotsOfRack(int rackId)
    {
        lock (_lock)
            _slots.RemoveAll(s => s.RackId == rackId);
    }

    // Id của model có setter private (chỉ constructor và cơ sở dữ liệu được gán). Store RAM đóng vai cơ sở dữ liệu
    // nên gán bằng reflection, ở một chỗ duy nhất này, thay vì mở setter ra cho cả code.
    private static void SetId(object entity, int id)
        => entity.GetType().GetProperty("Id")!.SetValue(entity, id);
}
