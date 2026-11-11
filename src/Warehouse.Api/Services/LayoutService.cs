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
            room = new Room(request.Cols, request.Rows, request.CellSize, request.MinGap, request.Margin, request.Cells);
        }
        catch (ArgumentException ex)
        {
            // Ví dụ Cells sai độ dài: ViewModel không kiểm tra được vì phụ thuộc Cols × Rows, Room tự chặn.
            return ServiceResult<RoomCreatedViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        store.SaveRoom(room);
        return ServiceResult<RoomCreatedViewModel>.Ok(new RoomCreatedViewModel(room.Id, room.Cells));
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
        var f = rack.GetFootprint(room.CellSize);
        return ServiceResult<PlacementCheckViewModel>.Ok(new PlacementCheckViewModel(reason is null, reason, f.Cols, f.Rows));
    }

    /// <summary>
    /// Đặt kệ thật. Thứ tự quan trọng:
    /// 1. Chặn trùng mã TRƯỚC khi kiểm tra đặt: PlacementValidator bỏ qua kệ cùng mã, nên nếu không chặn,
    ///    kệ mới trùng mã K1 đặt đè lên K1 vẫn được báo hợp lệ.
    /// 2. Kiểm tra lại bằng PlacementValidator: không tin kết quả validate frontend gọi lúc kéo,
    ///    vì giữa lúc kéo và lúc thả có thể đã có người đặt kệ khác vào đó.
    /// 3. Lưu kệ (SaveRoom gán Rack.Id) rồi MỚI sinh khung: CreateSlots lấy Rack.Id làm RackId,
    ///    sinh trước thì mọi khung có RackId = 0.
    /// </summary>
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

        room.Racks.Add(rack);
        store.SaveRoom(room);              // rack.Id có từ đây

        store.AddSlots(rack.CreateSlots());   // RackId = rack.Id

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
            .Select(r => RackViewModel.From(r, room.CellSize, store.GetSlotsOfRack(r.Id).Count))
            .ToList();

        return new RoomViewModel(room.Id, room.Cols, room.Rows, room.CellSize, room.Cells, room.MinGap, room.Margin, racks);
    }

    private static ServiceResult<T> NoRoom<T>()
        => ServiceResult<T>.Fail(ServiceError.NotFound, "Chưa có phòng. Tạo phòng trước (POST /api/layout/room).");
}
