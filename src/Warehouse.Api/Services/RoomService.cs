namespace Warehouse.Api.Services;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

/// <summary>Đọc danh sách phòng, một phòng, và tạo phòng trống.</summary>
public interface IRoomService
{
    /// <summary>Mọi phòng kèm số kệ, số khung, số tường/cột.</summary>
    Task<IReadOnlyList<RoomSummaryViewModel>> ListAsync(CancellationToken ct = default);

    /// <summary>Một phòng kèm tường/cột và kệ.</summary>
    /// <exception cref="NotFoundException">Không có phòng này.</exception>
    Task<RoomViewModel> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Tạo phòng trống. Tên không trùng (không phân biệt hoa thường).</summary>
    /// <exception cref="InvalidRequestException">Tên rỗng.</exception>
    /// <exception cref="ConflictException">Trùng tên.</exception>
    Task<RoomCreatedViewModel> CreateAsync(CreateRoomRequest request, CancellationToken ct = default);
}

public class RoomService(WarehouseDbContext db) : IRoomService
{
    public async Task<IReadOnlyList<RoomSummaryViewModel>> ListAsync(CancellationToken ct = default)
    {
        // Một câu SQL, không nạp kệ hay khung về bộ nhớ.
        return await db.Rooms
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoomSummaryViewModel(
                r.Id, r.Name, r.Width, r.Depth, r.Height, r.GridSize,
                r.Racks.Count,
                r.Racks.SelectMany(k => k.Levels).SelectMany(l => l.Slots).Count(),
                r.Obstacles.Count))
            .ToListAsync(ct);
    }

    public async Task<RoomViewModel> GetAsync(int id, CancellationToken ct = default)
    {
        var room = await db.Rooms
            .AsNoTracking()
            .AsSplitQuery()   // 3 câu SQL nhỏ thay vì 1 câu JOIN nhân bản dòng phòng theo từng kệ × tầng
            .Include(r => r.Obstacles)
            .Include(r => r.Racks).ThenInclude(k => k.Levels)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException($"Không tìm thấy kho có id {id}.");

        // Đếm khung theo kệ bằng một câu GROUP BY, không nạp cả bảng Slots.
        var slotCounts = await db.Slots
            .Where(s => s.Level.Rack.RoomId == id)
            .GroupBy(s => s.Level.RackId)
            .Select(g => new { RackId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RackId, x => x.Count, ct);

        return new RoomViewModel(
            room.Id, room.Name, room.Width, room.Depth, room.Height,
            room.GridSize, room.MinGap, room.Margin, room.Version,
            room.Obstacles.OrderBy(o => o.Id).Select(ObstacleViewModel.From).ToList(),
            room.Racks.OrderBy(k => k.Id)
                .Select(k => RackViewModel.From(k, slotCounts.GetValueOrDefault(k.Id)))
                .ToList());
    }

    public async Task<RoomCreatedViewModel> CreateAsync(CreateRoomRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new InvalidRequestException("Chưa nhập tên kho.");

        // Kiểm tra trước để báo lỗi rõ ràng; index UQ_Rooms_Name vẫn chặn khi hai người tạo cùng lúc (bắt ở dưới).
        if (await db.Rooms.AnyAsync(r => r.Name.ToLower() == name.ToLower(), ct))
            throw DuplicateName(name);

        var room = new Room
        {
            Name = name,
            Width = request.Width,
            Depth = request.Depth,
            Height = request.Height,
            GridSize = request.GridSize,
            MinGap = request.MinGap,
            Margin = request.Margin,
        };
        db.Rooms.Add(room);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UQ_Rooms_Name",
        })
        {
            throw DuplicateName(name);
        }

        return new RoomCreatedViewModel(room.Id);
    }

    private static ConflictException DuplicateName(string name) => new($"Đã có kho tên \"{name}\".");
using Warehouse.Api.Data;
using Warehouse.Api.ViewModels;

public interface IRoomService
{
    RoomViewModel? GetRoom(int roomId);    // null = không có kho này
}

public class RoomService(IWarehouseStore store) : IRoomService
{
    // Lấy một kho kèm kệ, tường/cột để giao diện vẽ.
    // Service: lấy dữ liệu từ store rồi đổi sang ViewModel. Không biết gì về HTTP (mã 200, 404 là việc của Controller).
    public RoomViewModel? GetRoom(int roomId)
    {
        // 1. Lấy từ store
        var room = store.GetRoom(roomId);
        if (room is null) return null;

        // 2. Đếm số khung của từng kệ
        var slotsPerRack = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList())
            .GroupBy(s => s.RackId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 3. Đổi từng kệ, từng tường/cột sang ViewModel
        var racks = room.Racks.Select(r =>
        {
            var bounds = r.GetBounds();   // chỗ kệ chiếm trên sàn, đã tính xoay 90° (luật nằm trong Rack)
            return new RackViewModel(
                r.Id, r.Code, r.X, r.Y, r.Rotation,
                r.Levels, r.LevelHeight, r.Bays, r.BayWidth, r.Depth,
                r.HasTop, r.MaxWeight, r.ProductTypeId,
                bounds.Width, bounds.Depth,
                slotsPerRack.GetValueOrDefault(r.Id));
        }).ToList();

        var obstacles = room.Obstacles
            .Select(o => new ObstacleViewModel(o.Id, o.Kind.ToString(), o.X, o.Y, o.Width, o.Depth))
            .ToList();

        // 4. Trả kết quả
        return new RoomViewModel(
            room.Id, room.Name, room.Width, room.Depth, room.GridSize,
            room.MinGap, room.Margin, room.Version, racks, obstacles);
    }

}
