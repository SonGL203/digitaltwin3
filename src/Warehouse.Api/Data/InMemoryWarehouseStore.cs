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
    private readonly List<Room> _rooms = new();
    private int _nextRoomId = 1;
    private int _nextRackId = 1;
    private int _nextObstacleId = 1;
    private int _nextSlotId = 1;

    private readonly List<PalletSize> _sizes = new();
    private readonly List<ProductType> _types = new();
    private readonly List<Batch> _batches = new();
    private readonly List<Pallet> _pallets = new();
    private int _nextSizeId = 1;
    private int _nextTypeId = 1;
    private int _nextBatchId = 1;
    private int _nextPalletId = 1;

    public IReadOnlyList<Room> GetRooms()
    {
        lock (_lock)
            return _rooms.ToList();
    }

    public Room? GetRoom(int id)
    {
        lock (_lock)
            return _rooms.FirstOrDefault(r => r.Id == id);
    }

    public void SaveRoom(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (_lock)
        {
            if (room.Id == 0)
            {
                SetId(room, _nextRoomId++);
                _rooms.Add(room);
            }

            foreach (var rack in room.Racks.Where(r => r.Id == 0))
                SetId(rack, _nextRackId++);

            foreach (var obstacle in room.Obstacles.Where(o => o.Id == 0))
                SetId(obstacle, _nextObstacleId++);

        }
    }

    public IReadOnlyList<Slot> GetAllSlots()
    {
        lock (_lock)
            return _slots.ToList();
    }

    public IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds)
    {
        lock (_lock)
            return _slots.Where(s => rackIds.Contains(s.RackId)).ToList();
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

    // Store RAM trả chính đối tượng đang giữ nên khung đã sửa rồi; không có gì để ghi lại.
    public void UpdateSlots(IEnumerable<Slot> slots) => ArgumentNullException.ThrowIfNull(slots);

    public IReadOnlyList<PalletSize> GetPalletSizes()
    {
        lock (_lock)
            return _sizes.ToList();
    }

    public void AddPalletSize(PalletSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        lock (_lock)
        {
            SetId(size, _nextSizeId++);
            _sizes.Add(size);
        }
    }

    public IReadOnlyList<ProductType> GetProductTypes()
    {
        lock (_lock)
            return _types.ToList();
    }

    public void AddProductType(ProductType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_lock)
        {
            SetId(type, _nextTypeId++);
            _types.Add(type);
        }
    }

    public void AddBatch(Batch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_lock)
        {
            SetId(batch, _nextBatchId++);
            foreach (var pallet in batch.Pallets)
            {
                SetId(pallet, _nextPalletId++);
                SetProperty(pallet, "BatchId", batch.Id);   // EF tự điền BatchId; store RAM làm thay
                _pallets.Add(pallet);
            }
            _batches.Add(batch);
        }
    }

    public Batch? GetBatch(int id)
    {
        lock (_lock)
            return _batches.FirstOrDefault(b => b.Id == id);
    }

    public int CountBatches()
    {
        lock (_lock)
            return _batches.Count;
    }

    public IReadOnlyList<Pallet> GetPallets(IReadOnlyCollection<int> ids)
    {
        lock (_lock)
            return _pallets.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).ToList();
    }

    /// <summary>
    /// Chỉ chạy work trong lock để không request nào chen vào giữa. KHÔNG rollback được: store RAM sửa thẳng
    /// đối tượng trong bộ nhớ, không có bản cũ để quay lại. Chấp nhận được vì store này chỉ dùng trong test,
    /// nơi các lần ghi trong RAM không lỗi giữa chừng; muốn kiểm tra rollback thì phải test trên PostgreSQL.
    /// </summary>
    public void InTransaction(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // lock của C# cho phép cùng một luồng vào lại (work gọi SaveRoom, AddSlots cũng lock) nên không tự khoá chết.
        lock (_lock)
            work();
    }

    // Id của model có setter private (chỉ constructor và cơ sở dữ liệu được gán). Store RAM đóng vai cơ sở dữ liệu
    // nên gán bằng reflection, ở một chỗ duy nhất này, thay vì mở setter ra cho cả code.
    private static void SetId(object entity, int id) => SetProperty(entity, "Id", id);

    private static void SetProperty(object entity, string name, object value)
        => entity.GetType().GetProperty(name)!.SetValue(entity, value);
}
