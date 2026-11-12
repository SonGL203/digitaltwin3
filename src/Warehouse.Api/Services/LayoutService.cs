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
    ServiceResult<LayoutCheckViewModel> ValidateLayout(int roomId, LayoutRequest request);
    ServiceResult<LayoutSavedViewModel> SaveLayout(int roomId, LayoutRequest request);
}

/// <summary>
/// Mặt bằng: các phòng và bản nháp của từng phòng. Chỉ điều phối: lấy dữ liệu từ store → hỏi PlacementValidator → lưu.
/// Không tự viết luật đặt kệ (luật nằm ở Rules/PlacementValidator), để kiểm tra thử và lưu thật luôn ra cùng kết quả.
/// Người dùng sửa trên bản nháp ở frontend; chỉ có hai đường vào để sửa phòng: ValidateLayout (không lưu) và SaveLayout.
/// </summary>
public class LayoutService(IWarehouseStore store) : ILayoutService
{
    /// <summary>Danh sách phòng để chọn: tên, kích thước, số kệ, số khung. Đếm khung bằng một lần đọc cho mọi phòng.</summary>
    public ServiceResult<IReadOnlyList<RoomSummaryViewModel>> GetRooms()
    {
        var slotsPerRack = store.GetAllSlots().GroupBy(s => s.RackId).ToDictionary(g => g.Key, g => g.Count());
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

    /// <summary>Tạo phòng mới (trống). Tên không được trùng phòng khác (không phân biệt hoa thường).</summary>
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
    /// Kiểm tra thử cả bản nháp (bóng mờ khi kéo kệ): không lưu, không kiểm tra Version. "Các kệ khác" là kệ TRONG
    /// BẢN NHÁP chứ không phải kệ đã lưu, nên kéo kệ sát một kệ vừa dời mà chưa lưu vẫn đúng.
    /// Trả kết quả cho từng kệ (kèm footprint đã tính xoay). Luôn 200 nếu có phòng.
    /// </summary>
    public ServiceResult<LayoutCheckViewModel> ValidateLayout(int roomId, LayoutRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<LayoutCheckViewModel>(roomId);

        var (plan, _, error) = BuildDraft(room, request);
        if (plan is null)
            return ServiceResult<LayoutCheckViewModel>.Ok(new LayoutCheckViewModel(false, error, []));

        var checks = plan.Racks.Select(x =>
        {
            var reason = PlacementValidator.Validate(plan.Draft, x.Item);
            var b = x.Item.GetBounds();
            return new RackCheckViewModel(x.Item.Code, reason is null, reason, b.Width, b.Depth);
        }).ToList();

        var first = checks.FirstOrDefault(c => !c.IsValid);
        return ServiceResult<LayoutCheckViewModel>.Ok(new LayoutCheckViewModel(
            first is null, first is null ? null : $"{first.Code}: {first.Reason}", checks));
    }

    /// <summary>
    /// Lưu cả bản nháp của phòng (nút Lưu nháp). Làm theo hai pha để lỗi không bao giờ làm phòng bị sửa dở:
    /// PHA 1 (chưa đụng dữ liệu thật): dựng một phòng NHÁP từ request rồi kiểm tra hết: phiên bản, tên, Id có tồn tại,
    /// mã kệ không trùng, vật cản nằm trong phòng, từng kệ qua PlacementValidator trên TOÀN BỘ trạng thái mới,
    /// kệ có hàng không bị xoá/đổi cấu hình. Kiểm tra sau khi dựng xong cả phòng nên không phụ thuộc thứ tự thao tác
    /// (dời K1 sang chỗ K2 vừa nhường vẫn hợp lệ).
    /// PHA 2: áp vào phòng thật trong MỘT transaction: xoá trước, rồi sửa/thêm, rồi sinh lại khung.
    /// </summary>
    public ServiceResult<LayoutSavedViewModel> SaveLayout(int roomId, LayoutRequest request)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<LayoutSavedViewModel>(roomId);

        if (request.Version != room.Version)
            return Fail(ServiceError.Conflict, "Phòng đã được lưu ở nơi khác sau lần bạn tải. Hãy tải lại phòng.");

        if (NameTaken(request.Name, exceptRoomId: roomId) is { } taken)
            return Fail(ServiceError.Conflict, taken);

        // ===== PHA 1: dựng phòng nháp và kiểm tra =====
        var (plan, kind, error) = BuildDraft(room, request);
        if (plan is null)
            return Fail(kind, error!);

        foreach (var (item, _) in plan.Racks)
        {
            if (PlacementValidator.Validate(plan.Draft, item) is { } reason)
                return Fail(ServiceError.Invalid, $"{item.Code}: {reason}");
        }

        // Kệ có hàng: được dời/xoay, nhưng không được xoá hay đổi cấu hình (sinh lại khung sẽ mất PalletId).
        var racksWithPallets = store.GetSlotsOfRacks(room.Racks.Select(k => k.Id).ToList())
            .Where(s => s.PalletId is not null).Select(s => s.RackId).ToHashSet();

        var removedRacks = room.Racks.Where(k => !plan.RackIds.Contains(k.Id)).ToList();
        if (removedRacks.FirstOrDefault(k => racksWithPallets.Contains(k.Id)) is { } busyRemoved)
            return Fail(ServiceError.Conflict, $"Kệ {busyRemoved.Code} đang có hàng, dời hàng trước khi xoá.");

        foreach (var (item, id) in plan.Racks.Where(x => x.Id is not null))
        {
            var current = room.Racks.First(k => k.Id == id);
            if (racksWithPallets.Contains(current.Id) && ConfigChanged(current, item))
                return Fail(ServiceError.Conflict, $"Kệ {current.Code} đang có hàng, dời hàng trước khi sửa cấu hình.");
        }

        // ===== PHA 2: áp vào phòng thật, một transaction =====
        var restructured = new List<Rack>();   // kệ có Id nhưng số tầng/ô/nóc đổi → phải sinh lại khung
        var added = new List<Rack>();

        store.InTransaction(() =>
        {
            room.Configure(request.Name, request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);

            // Xoá trước, lưu riêng một lần: kệ mới có thể dùng lại mã của kệ vừa xoá, mà index duy nhất (RoomId, Code)
            // sẽ báo trùng nếu INSERT đến trước DELETE trong cùng một lần lưu.
            foreach (var rack in removedRacks)
            {
                store.RemoveSlotsOfRack(rack.Id);
                room.Racks.Remove(rack);
            }
            foreach (var gone in room.Obstacles.Where(o => !plan.ObstacleIds.Contains(o.Id)).ToList())
                room.RemoveObstacle(gone.Id);
            store.SaveRoom(room);

            foreach (var (item, id) in plan.Obstacles)
            {
                if (id is { } oid)
                    room.Obstacles.First(o => o.Id == oid).Reconfigure(item.Kind, item.X, item.Y, item.Width, item.Depth);
                else
                    room.AddObstacle(item);
            }

            foreach (var (item, id) in plan.Racks)
            {
                if (id is { } rid)
                {
                    var rack = room.Racks.First(k => k.Id == rid);
                    var slotsChange = rack.Levels != item.Levels || rack.Bays != item.Bays || rack.HasTop != item.HasTop;
                    rack.Reconfigure(item.X, item.Y, item.Rotation, item.Levels, item.LevelHeight,
                                     item.Bays, item.BayWidth, item.Depth, item.HasTop, item.MaxWeight, item.ProductTypeId);
                    if (slotsChange)
                        restructured.Add(rack);
                }
                else
                {
                    room.Racks.Add(item);
                    added.Add(item);
                }
            }

            room.BumpVersion();
            store.SaveRoom(room);   // gán Id cho kệ và vật cản mới; sau đây mới sinh được khung (cần rack.Id)

            foreach (var rack in restructured)
                store.RemoveSlotsOfRack(rack.Id);
            foreach (var rack in restructured.Concat(added))
                store.AddSlots(rack.CreateSlots());
        });

        // Kệ mới là chính đối tượng trong `added` nên đã có Id sau SaveRoom.
        var rackIds = room.Racks.Select(k => new LayoutRackId(k.Id, k.Code)).ToList();
        var obstacleIds = plan.Obstacles.Select(x => x.Id ?? x.Item.Id).ToList();
        return ServiceResult<LayoutSavedViewModel>.Ok(new LayoutSavedViewModel(room.Version, rackIds, obstacleIds));
    }

    private sealed record DraftPlan(
        Room Draft,
        List<(Obstacle Item, int? Id)> Obstacles,
        List<(Rack Item, int? Id)> Racks,
        HashSet<int> ObstacleIds,
        HashSet<int> RackIds);

    /// <summary>
    /// Dựng phòng nháp từ request và kiểm tra những gì không cần PlacementValidator: giá trị hợp lệ, Id có thật,
    /// không gửi trùng Id, mã kệ không trùng, không đổi mã kệ đã có, vật cản nằm trong phòng.
    /// Chưa kiểm tra chỗ đặt kệ: SaveLayout dừng ở lỗi đầu tiên, ValidateLayout gom lỗi cho từng kệ.
    /// </summary>
    private static (DraftPlan? Plan, ServiceError Kind, string? Error) BuildDraft(Room room, LayoutRequest request)
    {
        Room draft;
        try
        {
            draft = new Room(request.Name, request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);
        }
        catch (ArgumentException ex)
        {
            return (null, ServiceError.Invalid, ex.Message);
        }

        var obstacles = new List<(Obstacle Item, int? Id)>();
        var seenObstacleIds = new HashSet<int>();
        foreach (var o in request.Obstacles)
        {
            if (ParseKind(o.Kind) is not { } kind)
                return (null, ServiceError.Invalid, "Loại vật cản chỉ được là Wall hoặc Column.");

            if (o.Id is { } oid)
            {
                if (room.Obstacles.All(x => x.Id != oid))
                    return (null, ServiceError.NotFound, $"Không có tường/cột Id {oid}.");
                if (!seenObstacleIds.Add(oid))
                    return (null, ServiceError.Invalid, $"Tường/cột Id {oid} bị gửi hai lần.");
            }

            try
            {
                var item = new Obstacle(room.Id, kind, o.X, o.Y, o.Width, o.Depth);
                draft.AddObstacle(item);   // chặn vật cản nằm ngoài phòng
                obstacles.Add((item, o.Id));
            }
            catch (ArgumentException ex)
            {
                return (null, ServiceError.Invalid, ex.Message);
            }
        }

        var racks = new List<(Rack Item, int? Id)>();
        var seenRackIds = new HashSet<int>();
        foreach (var r in request.Racks)
        {
            var code = r.Code.Trim();
            if (draft.Racks.Any(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase)))
                return (null, ServiceError.Conflict, $"Có hai kệ cùng mã {code}.");

            var (item, error) = BuildRack(room, r);
            if (item is null)
                return (null, ServiceError.Invalid, $"{code}: {error}");

            if (r.Id is { } rid)
            {
                var existing = room.Racks.FirstOrDefault(k => k.Id == rid);
                if (existing is null)
                    return (null, ServiceError.NotFound, $"Không có kệ Id {rid}.");
                if (!seenRackIds.Add(rid))
                    return (null, ServiceError.Invalid, $"Kệ Id {rid} bị gửi hai lần.");
                if (!string.Equals(existing.Code, item.Code, StringComparison.OrdinalIgnoreCase))
                    return (null, ServiceError.Invalid, $"Không đổi được mã kệ {existing.Code}. Xoá kệ rồi tạo lại.");
            }

            draft.Racks.Add(item);
            racks.Add((item, r.Id));
        }

        return (new DraftPlan(draft, obstacles, racks, seenObstacleIds, seenRackIds), ServiceError.None, null);
    }

    // Đổi bất kỳ thứ gì ngoài vị trí/hướng xoay (X, Y, Rotation) thì coi là đổi cấu hình.
    private static bool ConfigChanged(Rack a, Rack b)
        => a.Levels != b.Levels || a.Bays != b.Bays || a.HasTop != b.HasTop
           || a.ProductTypeId != b.ProductTypeId
           || !Tolerance.AreEqual(a.LevelHeight, b.LevelHeight)
           || !Tolerance.AreEqual(a.BayWidth, b.BayWidth)
           || !Tolerance.AreEqual(a.Depth, b.Depth)
           || !Tolerance.AreEqual(a.MaxWeight, b.MaxWeight);

    // Không dùng Enum.TryParse: nó nhận cả chuỗi số ("5") và ra một loại không tồn tại.
    private static ObstacleKind? ParseKind(string? kind) => kind switch
    {
        "Wall" => ObstacleKind.Wall,
        "Column" => ObstacleKind.Column,
        _ => null,
    };

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

    // Đếm khung của mọi kệ trong phòng bằng MỘT lần đọc (không đọc từng kệ một).
    private RoomViewModel ToViewModel(Room room)
    {
        var slotsPerRack = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList())
            .GroupBy(s => s.RackId).ToDictionary(g => g.Key, g => g.Count());
        var racks = room.Racks.Select(r => RackViewModel.From(r, slotsPerRack.GetValueOrDefault(r.Id))).ToList();
        var obstacles = room.Obstacles.Select(ObstacleViewModel.From).ToList();

        return new RoomViewModel(room.Id, room.Name, room.Width, room.Depth, room.GridSize, room.MinGap, room.Margin,
                                 room.Version, racks, obstacles);
    }

    /// <summary>Câu lỗi nếu tên đã có phòng khác dùng (không phân biệt hoa thường, đã cắt khoảng trắng), ngược lại null.</summary>
    private string? NameTaken(string? name, int? exceptRoomId)
    {
        var trimmed = (name ?? "").Trim();
        var taken = store.GetRooms().Any(r => r.Id != exceptRoomId
                                              && string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return taken ? $"Đã có phòng tên {trimmed}." : null;
    }

    private static ServiceResult<LayoutSavedViewModel> Fail(ServiceError error, string message)
        => ServiceResult<LayoutSavedViewModel>.Fail(error, message);

    private static ServiceResult<T> NoRoom<T>(int roomId)
        => ServiceResult<T>.Fail(ServiceError.NotFound, $"Không có phòng Id {roomId}.");
}
