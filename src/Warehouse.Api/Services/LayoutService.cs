namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

public interface ILayoutService
{
    ServiceResult<IReadOnlyList<RoomSummaryViewModel>> GetRooms();
    ServiceResult<RoomViewModel> GetLayout(int roomId);
    ServiceResult<RoomCreatedViewModel> CreateRoom(RoomRequest request);
    ServiceResult<RoomCreatedViewModel> UpdateRoom(int roomId, RoomRequest request);
    ServiceResult<PlacementCheckViewModel> ValidateRack(int roomId, RackRequest request);
    ServiceResult<RackCreatedViewModel> AddRack(int roomId, RackRequest request);
    ServiceResult<RackUpdatedViewModel> UpdateRack(int roomId, int id, RackRequest request);
    ServiceResult<RackCreatedViewModel> RemoveRack(int roomId, int id);
    ServiceResult<ObstacleCreatedViewModel> AddObstacle(int roomId, ObstacleRequest request);
    ServiceResult<ObstacleCreatedViewModel> RemoveObstacle(int roomId, int id);
}

/// <summary>
/// Mặt bằng: các phòng và đặt kệ. Chỉ điều phối: lấy dữ liệu từ store → hỏi PlacementValidator → lưu.
/// Không tự viết luật đặt kệ (luật nằm ở Rules/PlacementValidator), để kiểm tra thử và đặt thật luôn ra cùng kết quả.
/// </summary>
public class LayoutService(IWarehouseStore store) : ILayoutService
{
    /// <summary>Danh sách phòng để chọn: tên, kích thước, số kệ, số khung. Đếm khung bằng một lần đọc cho mọi phòng.</summary>
    public ServiceResult<IReadOnlyList<RoomSummaryViewModel>> GetRooms()
    {
        var slotsPerRack = SlotCountsByRack();
        var rooms = store.GetRooms()
            .Select(r => new RoomSummaryViewModel(
                r.Id, r.Name, r.Width, r.Depth, r.GridSize, r.Racks.Count, r.Obstacles.Count,
                r.Racks.Sum(k => slotsPerRack.GetValueOrDefault(k.Id))))
            .ToList();
        return ServiceResult<IReadOnlyList<RoomSummaryViewModel>>.Ok(rooms);
    }

    public ServiceResult<RoomViewModel> GetLayout(int roomId)
    {
        var room = store.GetRoom(roomId);
        return room is null
            ? NoRoom<RoomViewModel>(roomId)
            : ServiceResult<RoomViewModel>.Ok(ToViewModel(room));
    }

    /// <summary>Tạo phòng mới. Tên không được trùng phòng khác (không phân biệt hoa thường).</summary>
    public ServiceResult<RoomCreatedViewModel> CreateRoom(RoomRequest request)
    {
        if (NameTaken(request.Name, exceptRoomId: null) is { } taken)
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Conflict, taken);

        Room room;
        try
        {
            room = new Room(request.Name, request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);
        }
        catch (ArgumentException ex)
        {
            // ViewModel đã chặn [Range]; đây là lưới an toàn khi service bị gọi thẳng (test, seed).
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        store.SaveRoom(room);
        return ServiceResult<RoomCreatedViewModel>.Ok(new RoomCreatedViewModel(room.Id));
    }

    /// <summary>
    /// Kiểm tra thử (bóng mờ khi kéo kệ): không lưu, không sinh khung. Gọi được cho kệ mới lẫn kệ đang dời,
    /// vì PlacementValidator bỏ qua kệ cùng mã nên kệ đang dời không tự đè chính nó.
    /// </summary>
    public ServiceResult<PlacementCheckViewModel> ValidateRack(int roomId, RackRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<PlacementCheckViewModel>(roomId);

        var (rack, error) = BuildRack(room, request);
        if (rack is null)
            return ServiceResult<PlacementCheckViewModel>.Ok(new PlacementCheckViewModel(false, error, 0, 0));

        var reason = PlacementValidator.Validate(room, rack);
        var b = rack.GetBounds();
        return ServiceResult<PlacementCheckViewModel>.Ok(new PlacementCheckViewModel(reason is null, reason, b.Width, b.Depth));
    }

    public ServiceResult<RackCreatedViewModel> AddRack(int roomId, RackRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<RackCreatedViewModel>(roomId);

        var code = request.Code.Trim();
        if (room.Racks.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<RackCreatedViewModel>.Fail(ServiceError.Conflict, $"Đã có kệ mã {code}.");

        var (rack, error) = BuildRack(room, request);
        if (rack is null)
            return ServiceResult<RackCreatedViewModel>.Fail(ServiceError.Invalid, error!);

        var reason = PlacementValidator.Validate(room, rack);
        if (reason is not null)
            return ServiceResult<RackCreatedViewModel>.Fail(ServiceError.Invalid, reason);

        store.InTransaction(() =>
        {
            room.Racks.Add(rack);
            store.SaveRoom(room);                 // lần ghi 1: rack.Id có từ đây
            store.AddSlots(rack.CreateSlots());   // lần ghi 2: RackId = rack.Id
        });

        return ServiceResult<RackCreatedViewModel>.Ok(new RackCreatedViewModel(rack.Id, rack.Code));
    }

    /// <summary>
    /// Sửa phòng. Kiểm tra trên một phòng NHÁP trước rồi mới áp vào phòng thật, để lỡ không hợp lệ
    /// thì phòng thật không bị sửa dở (store RAM trả chính đối tượng đang giữ, sửa thẳng là sửa luôn dữ liệu).
    /// Chỉ đổi GridSize thì luôn qua; thu nhỏ phòng hoặc tăng MinGap có thể làm kệ đang có không còn hợp lệ.
    /// </summary>
    public ServiceResult<RoomCreatedViewModel> UpdateRoom(int roomId, RoomRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<RoomCreatedViewModel>(roomId);

        // Đổi tên trùng phòng KHÁC thì chặn; giữ nguyên tên của chính nó thì được.
        if (NameTaken(request.Name, exceptRoomId: roomId) is { } taken)
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Conflict, taken);

        Room draft;
        try
        {
            draft = new Room(request.Name, request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        draft.Obstacles.AddRange(room.Obstacles);
        draft.Racks.AddRange(room.Racks);

        if (room.Obstacles.Any(o => !o.Bounds.IsInside(draft.Bounds)))
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Invalid, "Có tường/cột nằm ngoài phòng mới.");

        foreach (var rack in room.Racks)
        {
            if (PlacementValidator.Validate(draft, rack) is { } reason)
                return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Invalid, $"{rack.Code}: {reason}");
        }

        room.Configure(request.Name, request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);
        store.SaveRoom(room);
        return ServiceResult<RoomCreatedViewModel>.Ok(new RoomCreatedViewModel(room.Id));
    }

    /// <summary>
    /// Thêm tường/cột. Chặn đè kệ ở đây (cần cả kệ lẫn vật cản, nên không đặt trong Room);
    /// Room.AddObstacle tự chặn vật cản nằm ngoài phòng.
    /// </summary>
    public ServiceResult<ObstacleCreatedViewModel> AddObstacle(int roomId, ObstacleRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<ObstacleCreatedViewModel>(roomId);

        // Không dùng Enum.TryParse: nó nhận cả chuỗi số ("5") và ra một loại không tồn tại.
        ObstacleKind? parsed = request.Kind switch
        {
            "Wall" => ObstacleKind.Wall,
            "Column" => ObstacleKind.Column,
            _ => null,
        };
        if (parsed is not { } kind)
            return ServiceResult<ObstacleCreatedViewModel>.Fail(ServiceError.Invalid, "Loại vật cản chỉ được là Wall hoặc Column.");

        Obstacle obstacle;
        try
        {
            obstacle = new Obstacle(room.Id, kind, request.X, request.Y, request.Width, request.Depth);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<ObstacleCreatedViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        if (room.Racks.FirstOrDefault(r => r.GetBounds().Overlaps(obstacle.Bounds)) is { } hit)
            return ServiceResult<ObstacleCreatedViewModel>.Fail(ServiceError.Invalid, $"Vật cản đè lên kệ {hit.Code}.");

        try
        {
            room.AddObstacle(obstacle);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<ObstacleCreatedViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        store.SaveRoom(room);
        return ServiceResult<ObstacleCreatedViewModel>.Ok(new ObstacleCreatedViewModel(obstacle.Id));
    }

    public ServiceResult<ObstacleCreatedViewModel> RemoveObstacle(int roomId, int id)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<ObstacleCreatedViewModel>(roomId);

        if (!room.RemoveObstacle(id))
            return ServiceResult<ObstacleCreatedViewModel>.Fail(ServiceError.NotFound, $"Không có tường/cột Id {id}.");

        store.SaveRoom(room);   // EF tự DELETE vật cản bị bỏ khỏi danh sách (quan hệ bắt buộc với phòng)
        return ServiceResult<ObstacleCreatedViewModel>.Ok(new ObstacleCreatedViewModel(id));
    }

    /// <summary>
    /// Sửa kệ. Thứ tự:
    /// 1. Không cho đổi mã: PlacementValidator nhận ra "chính mình" bằng mã; đổi K1 → K9 thì K1 cũ bị coi là kệ khác.
    /// 2. Kiểm tra trên kệ NHÁP (cùng mã nên validator bỏ qua kệ cũ), chưa đụng kệ thật.
    /// 3. Kệ có hàng → 409: sinh lại khung sẽ mất PalletId, tức mất vị trí hàng.
    /// 4. Trong MỘT transaction: áp cấu hình, lưu, xoá khung cũ, sinh khung mới; lỗi giữa chừng thì không có kệ mất khung.
    /// </summary>
    public ServiceResult<RackUpdatedViewModel> UpdateRack(int roomId, int id, RackRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<RackUpdatedViewModel>(roomId);

        var rack = room.Racks.FirstOrDefault(r => r.Id == id);
        if (rack is null)
            return ServiceResult<RackUpdatedViewModel>.Fail(ServiceError.NotFound, $"Không có kệ Id {id}.");

        if (!string.Equals(request.Code.Trim(), rack.Code, StringComparison.OrdinalIgnoreCase))
            return ServiceResult<RackUpdatedViewModel>.Fail(ServiceError.Invalid,
                "Không đổi được mã kệ khi sửa. Xoá kệ rồi tạo lại.");

        var (draft, error) = BuildRack(room, request);
        if (draft is null)
            return ServiceResult<RackUpdatedViewModel>.Fail(ServiceError.Invalid, error!);

        if (PlacementValidator.Validate(room, draft) is { } reason)
            return ServiceResult<RackUpdatedViewModel>.Fail(ServiceError.Invalid, reason);

        if (store.GetSlotsOfRack(id).Any(s => s.PalletId is not null))
            return ServiceResult<RackUpdatedViewModel>.Fail(ServiceError.Conflict,
                "Kệ đang có hàng, dời hàng trước khi sửa.");

        List<Slot> slots = [];
        store.InTransaction(() =>
        {
            rack.Reconfigure(draft.X, draft.Y, draft.Rotation, draft.Levels, draft.LevelHeight,
                             draft.Bays, draft.BayWidth, draft.Depth, draft.HasTop, draft.MaxWeight, draft.ProductTypeId);
            store.SaveRoom(room);
            store.RemoveSlotsOfRack(id);
            slots = rack.CreateSlots();
            store.AddSlots(slots);
        });

        return ServiceResult<RackUpdatedViewModel>.Ok(new RackUpdatedViewModel(id, slots.Count));
    }

    /// <summary>
    /// Xoá kệ cùng mọi khung của nó. Kệ đang có hàng thì chặn (409): xoá khung là mất PalletId, tức mất vị trí hàng.
    /// Xoá khung rồi mới xoá kệ, trong MỘT transaction: lỗi giữa chừng thì không có kệ mất khung hay khung mồ côi.
    /// (Trên PostgreSQL, xoá kệ cũng tự xoá khung nhờ cascade; gọi RemoveSlotsOfRack để store RAM cũng đúng.)
    /// </summary>
    public ServiceResult<RackCreatedViewModel> RemoveRack(int roomId, int id)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<RackCreatedViewModel>(roomId);

        var rack = room.Racks.FirstOrDefault(r => r.Id == id);
        if (rack is null)
            return ServiceResult<RackCreatedViewModel>.Fail(ServiceError.NotFound, $"Không có kệ Id {id}.");

        if (store.GetSlotsOfRack(id).Any(s => s.PalletId is not null))
            return ServiceResult<RackCreatedViewModel>.Fail(ServiceError.Conflict,
                "Kệ đang có hàng, dời hàng trước khi xoá.");

        store.InTransaction(() =>
        {
            store.RemoveSlotsOfRack(id);
            room.Racks.Remove(rack);
            store.SaveRoom(room);   // EF tự DELETE kệ bị bỏ khỏi danh sách (quan hệ bắt buộc với phòng)
        });

        return ServiceResult<RackCreatedViewModel>.Ok(new RackCreatedViewModel(id, rack.Code));
    }

    // Đổi ViewModel sang Rack. Constructor Rack tự kiểm tra (mã rỗng, Rotation khác 0/90…);
    // bắt ArgumentException ở đây để trả câu lý do thay vì lỗi 500.
    private static (Rack? Rack, string? Error) BuildRack(Room room, RackRequest r)
    {
        try
        {
            var rack = new Rack(room.Id, r.Code, r.X, r.Y, r.Rotation,
                                r.Levels, r.LevelHeight, r.Bays, r.BayWidth, r.Depth,
                                r.HasTop, r.MaxWeight, r.ProductTypeId);
            return (rack, null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    private RoomViewModel ToViewModel(Room room)
    {
        var racks = room.Racks
            .Select(r => RackViewModel.From(r, store.GetSlotsOfRack(r.Id).Count))
            .ToList();
        var obstacles = room.Obstacles.Select(ObstacleViewModel.From).ToList();

        return new RoomViewModel(room.Id, room.Name, room.Width, room.Depth, room.GridSize, room.MinGap, room.Margin,
                                 racks, obstacles);
    }

    /// <summary>Câu lỗi nếu tên đã có phòng khác dùng (không phân biệt hoa thường, đã cắt khoảng trắng), ngược lại null.</summary>
    private string? NameTaken(string? name, int? exceptRoomId)
    {
        var trimmed = (name ?? "").Trim();
        var taken = store.GetRooms().Any(r => r.Id != exceptRoomId
                                              && string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return taken ? $"Đã có phòng tên {trimmed}." : null;
    }

    private Dictionary<int, int> SlotCountsByRack()
        => store.GetAllSlots().GroupBy(s => s.RackId).ToDictionary(g => g.Key, g => g.Count());

    private static ServiceResult<T> NoRoom<T>(int roomId)
        => ServiceResult<T>.Fail(ServiceError.NotFound, $"Không có phòng Id {roomId}.");
}
