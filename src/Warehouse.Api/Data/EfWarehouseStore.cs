namespace Warehouse.Api.Data;

using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Models;

/// <summary>
/// Store lưu thật xuống PostgreSQL qua EF Core. Cùng interface với InMemoryWarehouseStore nên
/// LayoutService không phải sửa gì; chỉ đổi một dòng nối dây trong Program.cs.
///
/// Đăng ký Scoped (mỗi request một DbContext): DbContext không an toàn khi nhiều luồng dùng chung,
/// và phải ngắn đời để không giữ dữ liệu cũ. Trong một request, đối tượng đọc ra được DbContext "theo dõi":
/// sửa nó (ví dụ room.Racks.Add) rồi gọi Save là EF tự biết phải INSERT/UPDATE gì.
/// </summary>
public class EfWarehouseStore(WarehouseDbContext db) : IWarehouseStore
{
    public IReadOnlyList<Room> GetRooms()
        => RoomsWithChildren().OrderBy(r => r.Id).ToList();

    public Room? GetRoom(int id)
        => RoomsWithChildren().FirstOrDefault(r => r.Id == id);

    // Include nạp luôn kệ và vật cản (không thì hai danh sách rỗng). Sắp theo Id để thứ tự kệ ổn định
    // giữa các lần đọc, giống store RAM (thứ tự thêm vào); Allocator duyệt theo thứ tự này.
    // AsSplitQuery: hai Include trong một câu SQL sẽ nhân chéo kệ × vật cản; tách thành từng câu cho gọn.
    private IQueryable<Room> RoomsWithChildren()
        => db.Rooms
             .Include(r => r.Racks.OrderBy(k => k.Id))
             .Include(r => r.Obstacles.OrderBy(o => o.Id))
             .AsSplitQuery();

    public void SaveRoom(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        // Phòng đọc từ GetRoom trong cùng request đã được theo dõi: chỉ cần SaveChanges,
        // EF tự phát hiện kệ mới trong Racks (Id = 0) và INSERT nó. Phòng chưa được theo dõi thì
        // Id = 0 là phòng mới (Add), ngược lại là phòng đã có (Update cả phòng lẫn kệ).
        if (db.Entry(room).State == EntityState.Detached)
        {
            if (room.Id == 0)
                db.Rooms.Add(room);
            else
                db.Rooms.Update(room);
        }

        db.SaveChanges();   // DB sinh Id, EF ghi ngược vào room.Id và rack.Id
    }

    public IReadOnlyList<Slot> GetAllSlots()
        => db.Slots.OrderBy(s => s.Id).ToList();

    public IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds)
        => db.Slots.Where(s => rackIds.Contains(s.RackId)).OrderBy(s => s.Id).ToList();

    public void AddSlots(IEnumerable<Slot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        db.Slots.AddRange(slots);
        db.SaveChanges();
    }

    public void RemoveSlotsOfRack(int rackId)
    {
        // Đọc ra rồi RemoveRange thay vì ExecuteDelete: ExecuteDelete xoá thẳng trong DB nhưng các khung
        // đang được theo dõi trong request vẫn còn trong bộ nhớ EF, dễ lệch. Kệ chỉ có vài chục khung nên không chậm.
        var slots = db.Slots.Where(s => s.RackId == rackId).ToList();
        db.Slots.RemoveRange(slots);
        db.SaveChanges();
    }

    public void UpdateSlots(IEnumerable<Slot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        // Khung đọc từ GetAllSlots/GetSlotsOfRacks trong cùng request đã được theo dõi nên SaveChanges là đủ;
        // khung chưa được theo dõi thì Update để EF ghi cả hàng.
        foreach (var slot in slots)
        {
            if (db.Entry(slot).State == EntityState.Detached)
                db.Slots.Update(slot);
        }
        db.SaveChanges();
    }

    public IReadOnlyList<PalletSize> GetPalletSizes()
        => db.PalletSizes.OrderBy(s => s.Id).ToList();

    public void AddPalletSize(PalletSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        db.PalletSizes.Add(size);
        db.SaveChanges();
    }

    public IReadOnlyList<ProductType> GetProductTypes()
        => db.ProductTypes.OrderBy(t => t.Id).ToList();

    public void AddProductType(ProductType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        db.ProductTypes.Add(type);
        db.SaveChanges();
    }

    public void AddBatch(Batch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        db.Batches.Add(batch);   // pallet trong batch.Pallets được thêm theo; EF điền BatchId
        db.SaveChanges();
    }

    public Batch? GetBatch(int id)
        => db.Batches.Include(b => b.Pallets).ThenInclude(p => p.Size).FirstOrDefault(b => b.Id == id);

    public int CountBatches() => db.Batches.Count();

    public IReadOnlyList<Pallet> GetPallets(IReadOnlyCollection<int> ids)
        => db.Pallets.Include(p => p.Size).Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).ToList();

    public void InTransaction(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Mở transaction của PostgreSQL. Mọi SaveChanges bên trong work đều chạy trong transaction này:
        // dữ liệu đã gửi xuống DB nhưng người khác chưa thấy, cho tới khi Commit.
        using var transaction = db.Database.BeginTransaction();

        work();

        // Chỉ tới được đây khi work chạy hết không lỗi. Nếu work ném lỗi thì không Commit;
        // khi rời khỏi hàm, "using" huỷ transaction và PostgreSQL tự rollback mọi thứ đã ghi bên trong.
        transaction.Commit();
    }
}
