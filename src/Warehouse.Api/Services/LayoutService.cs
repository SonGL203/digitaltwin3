namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

public interface ILayoutService
{
    ServiceResult<RoomViewModel> GetLayout();
    ServiceResult<RoomCreatedViewModel> CreateRoom(RoomRequest request);
    ServiceResult<PlacementCheckViewModel> ValidateRack(RackRequest request);
    ServiceResult<RackCreatedViewModel> AddRack(RackRequest request);
}

/// <summary>
/// Mặt bằng: phòng và đặt kệ. Chỉ điều phối: lấy dữ liệu từ store → hỏi PlacementValidator → lưu.
/// Không tự viết luật đặt kệ (luật nằm ở Rules/PlacementValidator), để kiểm tra thử và đặt thật luôn ra cùng kết quả.
/// </summary>
public class LayoutService(IWarehouseStore store) : ILayoutService
{
    public ServiceResult<RoomViewModel> GetLayout()
    {
        var room = store.GetRoom();
        return room is null
            ? NoRoom<RoomViewModel>()
            : ServiceResult<RoomViewModel>.Ok(ToViewModel(room));
    }

    /// <summary>
    /// Tạo phòng. Hiện chỉ có một phòng; tạo lần hai báo Conflict thay vì ghi đè,
    /// vì ghi đè sẽ làm mồ côi các kệ và khung của phòng cũ.
    /// </summary>
    public ServiceResult<RoomCreatedViewModel> CreateRoom(RoomRequest request)
    {
        if (store.GetRoom() is not null)
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Conflict, "Đã có phòng.");

        Room room;
        try
        {
            room = new Room(request.Width, request.Depth, request.GridSize, request.MinGap, request.Margin);
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
    public ServiceResult<PlacementCheckViewModel> ValidateRack(RackRequest request)
    {
        var room = store.GetRoom();
        if (room is null)
            return NoRoom<PlacementCheckViewModel>();

        var (rack, error) = BuildRack(room, request);
        if (rack is null)
            return ServiceResult<PlacementCheckViewModel>.Ok(new PlacementCheckViewModel(false, error, 0, 0));

        var reason = PlacementValidator.Validate(room, rack);
        var b = rack.GetBounds();
        return ServiceResult<PlacementCheckViewModel>.Ok(new PlacementCheckViewModel(reason is null, reason, b.Width, b.Depth));
    }

    public ServiceResult<RackCreatedViewModel> AddRack(RackRequest request)
    {
        var room = store.GetRoom();
        if (room is null)
            return NoRoom<RackCreatedViewModel>();

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

        return new RoomViewModel(room.Id, room.Width, room.Depth, room.GridSize, room.MinGap, room.Margin, racks, obstacles);
    }

    private static ServiceResult<T> NoRoom<T>()
        => ServiceResult<T>.Fail(ServiceError.NotFound, "Chưa có phòng. Tạo phòng trước (POST /api/layout/room).");
}
