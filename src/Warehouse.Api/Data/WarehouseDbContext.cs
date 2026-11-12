namespace Warehouse.Api.Data;

using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Models;

/// <summary>
/// Bản đồ giữa Models và bảng PostgreSQL. Toàn bộ cấu hình viết bằng Fluent API ở đây, KHÔNG dùng attribute
/// ([Key], [Table]…) trên Models, để Models không phụ thuộc EF (luật tầng, CLAUDE.md mục 2).
///
/// Models có setter private và constructor có tham số. EF Core tự gọi constructor đó khi đọc từ DB
/// ("constructor binding") vì tên tham số trùng tên thuộc tính (width ↔ Width, rackId ↔ RackId…);
/// các thuộc tính không có trong constructor (Id, AssignedSize, PalletId) EF gán qua setter private.
/// Nên không cần thêm constructor rỗng vào Models.
/// Thuộc tính chỉ tính ra (Room.Bounds, Obstacle.Bounds) không có setter nên EF bỏ qua, không thành cột.
///
/// Hiện mới map phần mặt bằng (Room, Rack, Obstacle, Slot), khớp với IWarehouseStore. Pallet, lô, type thêm ở ngày 3.
/// </summary>
public class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<Obstacle> Obstacles => Set<Obstacle>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<PalletSize> PalletSizes => Set<PalletSize>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Pallet> Pallets => Set<Pallet>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Room>(room =>
        {
            room.HasKey(r => r.Id);
            room.Property(r => r.Name).IsRequired().HasMaxLength(50);
            // Lưới an toàn ở tầng DB: tên phòng không trùng. LayoutService đã chặn trước (và chặn cả khác hoa thường);
            // index này bắt trường hợp hai request tạo cùng tên đến cùng lúc.
            room.HasIndex(r => r.Name).IsUnique();
            // Width, Depth, GridSize, MinGap, Margin (m): EF tự map double → double precision.

            // Room.Racks chỉ có getter ({ get; } = new()); EF dùng field ẩn phía sau để nạp danh sách.
            // Rack không có thuộc tính trỏ ngược về Room, chỉ có RoomId, nên WithOne() để trống.
            // Xoá phòng thì xoá luôn kệ (Cascade): kệ không thể tồn tại ngoài phòng.
            room.HasMany(r => r.Racks)
                .WithOne()
                .HasForeignKey(k => k.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tường/cột thuộc phòng giống kệ.
            room.HasMany(r => r.Obstacles)
                .WithOne()
                .HasForeignKey(o => o.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Obstacle>(obstacle =>
        {
            obstacle.HasKey(o => o.Id);
            // Lưu "Wall"/"Column" thay vì 0/1: đọc bảng là hiểu ngay,
            // và chèn thêm loại mới vào enum sau này không làm lệch dữ liệu cũ.
            obstacle.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
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

            // Mã khung (K1-T1-R1) duy nhất TRONG MỘT KỆ. Không để duy nhất toàn DB: hai phòng đều có kệ K1
            // thì cùng có khung K1-T1-R1.
            slot.HasIndex(s => new { s.RackId, s.Code }).IsUnique();

            // Khung trỏ tới pallet nó đang giữ. Restrict: không cho xoá pallet khi còn khung trỏ tới (phải Remove() khung trước).
            slot.HasOne<Pallet>()
                .WithMany()
                .HasForeignKey(s => s.PalletId)
                .OnDelete(DeleteBehavior.Restrict);

            // Một pallet chỉ nằm trong một khung. Khung trống (PalletId null) không bị ràng buộc: PostgreSQL cho nhiều NULL.
            slot.HasIndex(s => s.PalletId).IsUnique();
        });

        // Type pallet (A, B, …) do PalletSizer sinh, dùng chung cho mọi phòng; mã không trùng.
        model.Entity<PalletSize>(size =>
        {
            size.HasKey(s => s.Id);
            size.Property(s => s.Code).IsRequired().HasMaxLength(10);
            size.Property(s => s.Color).IsRequired().HasMaxLength(20);
            size.HasIndex(s => s.Code).IsUnique();
        });

        model.Entity<ProductType>(type =>
        {
            type.HasKey(t => t.Id);
            type.Property(t => t.Code).IsRequired().HasMaxLength(20);
            type.Property(t => t.Name).IsRequired().HasMaxLength(50);
            type.HasIndex(t => t.Code).IsUnique();
        });

        // Kệ chuyên loại hàng: Restrict, không xoá được loại hàng khi còn kệ/pallet dùng.
        model.Entity<Rack>()
            .HasOne<ProductType>()
            .WithMany()
            .HasForeignKey(k => k.ProductTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Batch>(batch =>
        {
            batch.HasKey(b => b.Id);
            batch.Property(b => b.Code).IsRequired().HasMaxLength(20);
            batch.HasIndex(b => b.Code).IsUnique();
            // ReceivedAt là DateTime: Npgsql chỉ nhận Kind = Utc cho timestamptz, nên service luôn dùng DateTime.UtcNow.

            // Xoá lô thì xoá pallet của lô (khung đang giữ pallet vẫn chặn nhờ Restrict ở trên).
            batch.HasMany(b => b.Pallets)
                 .WithOne()
                 .HasForeignKey(p => p.BatchId)
                 .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Pallet>(pallet =>
        {
            pallet.HasKey(p => p.Id);
            pallet.Property(p => p.Code).IsRequired().HasMaxLength(30);
            pallet.HasIndex(p => p.Code).IsUnique();

            // Pallet trỏ tới type; Size là thuộc tính điều hướng (không có trong constructor công khai).
            pallet.HasOne(p => p.Size)
                  .WithMany()
                  .HasForeignKey(p => p.SizeId)
                  .OnDelete(DeleteBehavior.Restrict);

            pallet.HasOne<ProductType>()
                  .WithMany()
                  .HasForeignKey(p => p.ProductTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
