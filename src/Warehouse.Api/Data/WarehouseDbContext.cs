namespace Warehouse.Api.Data;

using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Models;

/// <summary>
/// Bản đồ giữa Models và bảng PostgreSQL. Toàn bộ cấu hình viết bằng Fluent API ở đây, KHÔNG dùng attribute
/// ([Key], [Table]…) trên Models, để Models không phụ thuộc EF (luật tầng, CLAUDE.md mục 2).
///
/// Models có setter private và constructor có tham số. EF Core tự gọi constructor đó khi đọc từ DB
/// ("constructor binding") vì tên tham số trùng tên thuộc tính (cols ↔ Cols, rackId ↔ RackId…);
/// các thuộc tính không có trong constructor (Id, AssignedSize, PalletId) EF gán qua setter private.
/// Nên không cần thêm constructor rỗng vào Models.
///
/// Hiện mới map phần mặt bằng (Room, Rack, Slot), khớp với IWarehouseStore. Pallet, lô, type thêm ở ngày 3.
/// </summary>
public class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<Slot> Slots => Set<Slot>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Room>(room =>
        {
            room.HasKey(r => r.Id);
            room.Property(r => r.Cells).IsRequired();

            // Room.Racks chỉ có getter ({ get; } = new()); EF dùng field ẩn phía sau để nạp danh sách.
            // Rack không có thuộc tính trỏ ngược về Room, chỉ có RoomId, nên WithOne() để trống.
            // Xoá phòng thì xoá luôn kệ (Cascade): kệ không thể tồn tại ngoài phòng.
            room.HasMany(r => r.Racks)
                .WithOne()
                .HasForeignKey(k => k.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Rack>(rack =>
        {
            rack.HasKey(k => k.Id);
            rack.Property(k => k.Code).IsRequired().HasMaxLength(20);

            // Lưới an toàn ở tầng DB: mã kệ không trùng trong một phòng. LayoutService đã chặn trước
            // (và chặn cả khác hoa thường); index này bắt trường hợp hai request đặt cùng mã đến cùng lúc.
            rack.HasIndex(k => new { k.RoomId, k.Code }).IsUnique();
        });

        model.Entity<Slot>(slot =>
        {
            slot.HasKey(s => s.Id);
            slot.Property(s => s.Code).IsRequired().HasMaxLength(40);
            slot.Property(s => s.AssignedSize).HasMaxLength(10);

            // Khung thuộc một kệ; xoá kệ thì xoá khung của nó.
            slot.HasOne<Rack>()
                .WithMany()
                .HasForeignKey(s => s.RackId)
                .OnDelete(DeleteBehavior.Cascade);

            // Mã khung (K1-T1-R1) duy nhất vì mã kệ đã duy nhất.
            slot.HasIndex(s => s.Code).IsUnique();

            // PalletId chưa có khoá ngoại vì bảng Pallet chưa map; thêm ở ngày 3.
        });
    }
}
