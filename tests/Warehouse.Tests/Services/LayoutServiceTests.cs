using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;
using Warehouse.Tests.Fakes;

namespace Warehouse.Tests.Services;

// Phòng 10 m × 5 m, lưới 0,5 m, lối đi tối thiểu 1,0 m. Mọi toạ độ bằng mét.
// Kệ mẫu: 3 tầng × 3 ô, mỗi ô rộng 1,0 m → dài 3,0 m theo X, sâu 1,0 m theo Y.
// Có nóc nên 3 × 3 + 3 = 12 khung.
// Test trên store RAM: không cần PostgreSQL, mỗi test một kho sạch.
public class LayoutServiceTests
{
    private static RoomRequest RoomReq(double width = 10, double depth = 5) => new()
    {
        Width = width, Depth = depth, GridSize = 0.5, MinGap = 1.0, Margin = 0.05,
    };

    private static RackRequest RackReq(string code, double x, double y, int rotation = 0) => new()
    {
        Code = code, X = x, Y = y, Rotation = rotation,
        Levels = 3, LevelHeight = 1.2, Bays = 3, BayWidth = 1.0, Depth = 1.0, HasTop = true, MaxWeight = 1000,
    };

    // Chưa có API thêm tường/cột nên test thêm thẳng vào phòng đã lưu rồi lưu lại.
    private static void AddObstacle(IWarehouseStore store, ObstacleKind kind, double x, double y, double width, double depth)
    {
        var room = store.GetRoom()!;
        room.AddObstacle(new Obstacle(room.Id, kind, x, y, width, depth));
        store.SaveRoom(room);
    }

    // Kho đã có phòng trống.
    private static (LayoutService Service, IWarehouseStore Store) WithRoom()
    {
        var store = new InMemoryWarehouseStore();
        var service = new LayoutService(store);
        Assert.True(service.CreateRoom(RoomReq()).IsSuccess);
        return (service, store);
    }

    // ---- GetLayout ----

    [Fact]
    public void GetLayout_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).GetLayout();

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void GetLayout_AfterAddRack_ReturnsRackWithFootprintInMetersAndSlotCount()
    {
        var (service, _) = WithRoom();
        service.AddRack(RackReq("K1", 0, 0));

        var rack = Assert.Single(service.GetLayout().Value!.Racks);

        Assert.Equal("K1", rack.Code);
        Assert.Equal(3.0, rack.FootprintWidth);
        Assert.Equal(1.0, rack.FootprintDepth);
        Assert.Equal(12, rack.SlotCount);
    }

    [Fact]
    public void GetLayout_ReturnsRoomSizesAndObstacles()
    {
        var (service, store) = WithRoom();
        AddObstacle(store, ObstacleKind.Column, 6.5, 2.0, 0.4, 0.4);

        var layout = service.GetLayout().Value!;

        Assert.Equal(10, layout.Width);
        Assert.Equal(5, layout.Depth);
        Assert.Equal(0.5, layout.GridSize);
        var column = Assert.Single(layout.Obstacles);
        Assert.Equal("Column", column.Kind);
        Assert.Equal(6.5, column.X);
    }

    // ---- CreateRoom ----

    [Fact]
    public void CreateRoom_Valid_ReturnsId()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).CreateRoom(RoomReq());

        Assert.True(result.IsSuccess);
        Assert.NotEqual(0, result.Value!.Id);
    }

    [Fact]
    public void CreateRoom_Twice_ReturnsConflict()
    {
        var (service, _) = WithRoom();

        var result = service.CreateRoom(RoomReq());

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public void CreateRoom_InvalidSize_ReturnsInvalidAndSavesNothing()
    {
        // Gọi thẳng service nên bỏ qua [Range]; constructor Room chặn, service đổi thành lý do.
        var store = new InMemoryWarehouseStore();

        var result = new LayoutService(store).CreateRoom(RoomReq(width: 0));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Null(store.GetRoom());
    }

    // ---- ValidateRack (kiểm tra thử, không lưu) ----

    [Fact]
    public void ValidateRack_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).ValidateRack(RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void ValidateRack_FreeSpot_IsValidWithFootprint()
    {
        var (service, _) = WithRoom();

        var check = service.ValidateRack(RackReq("K1", 0, 0)).Value!;

        Assert.True(check.IsValid);
        Assert.Null(check.Reason);
        Assert.Equal(3.0, check.FootprintWidth);
        Assert.Equal(1.0, check.FootprintDepth);
    }

    [Fact]
    public void ValidateRack_RotatedRack_SwapsFootprint()
    {
        var (service, _) = WithRoom();

        var check = service.ValidateRack(RackReq("K1", 0, 0, rotation: 90)).Value!;

        Assert.Equal(1.0, check.FootprintWidth);
        Assert.Equal(3.0, check.FootprintDepth);
    }

    [Fact]
    public void ValidateRack_OnWall_IsInvalidButStillSuccessAndSavesNothing()
    {
        var (service, store) = WithRoom();
        AddObstacle(store, ObstacleKind.Wall, 1.0, 0, 0.2, 5);

        var result = service.ValidateRack(RackReq("K1", 0, 0));

        // Đặt không được vẫn là câu trả lời bình thường (IsSuccess), chỉ IsValid = false.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsValid);
        Assert.Equal("Kệ đè tường tại (1; 0) m.", result.Value.Reason);
        Assert.Empty(store.GetRoom()!.Racks);
        Assert.Empty(store.GetAllSlots());
    }

    [Fact]
    public void ValidateRack_BadRotation_IsInvalidInsteadOfThrowing()
    {
        // Gọi thẳng service nên bỏ qua [AllowedValues]; constructor Rack chặn, service đổi thành lý do.
        var (service, _) = WithRoom();

        var check = service.ValidateRack(RackReq("K1", 0, 0, rotation: 45)).Value!;

        Assert.False(check.IsValid);
        Assert.NotNull(check.Reason);
    }

    [Fact]
    public void ValidateRack_MovingExistingRack_DoesNotCollideWithItself()
    {
        // K1 ở x 0 → 3. Dời K1 sang x 1 → 4, chồng lên chỗ cũ: phải hợp lệ vì cùng mã.
        var (service, _) = WithRoom();
        service.AddRack(RackReq("K1", 0, 0));

        var check = service.ValidateRack(RackReq("K1", 1.0, 0)).Value!;

        Assert.True(check.IsValid);
    }

    [Fact]
    public void ValidateRack_NewRackTooClose_ReportsMissingAisle()
    {
        // K1 ở y 0 → 1; K2 ở y 1,5 chỉ cách 0,5 m, cần 1,0 m.
        var (service, _) = WithRoom();
        service.AddRack(RackReq("K1", 0, 0));

        var check = service.ValidateRack(RackReq("K2", 0, 1.5)).Value!;

        Assert.False(check.IsValid);
        Assert.Equal("Thiếu lối đi với K1.", check.Reason);
    }

    // ---- AddRack (đặt thật) ----

    [Fact]
    public void AddRack_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).AddRack(RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void AddRack_Valid_ReturnsIdAndTrimmedCode()
    {
        var (service, store) = WithRoom();

        var result = service.AddRack(RackReq("  K1 ", 0, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal("K1", result.Value!.Code);
        Assert.Equal(store.GetRoom()!.Racks.Single().Id, result.Value.Id);
    }

    [Fact]
    public void AddRack_Valid_KeepsPositionInMeters()
    {
        var (service, store) = WithRoom();

        service.AddRack(RackReq("K1", 2.25, 0.75));

        var rack = store.GetRoom()!.Racks.Single();
        Assert.Equal(2.25, rack.X);
        Assert.Equal(0.75, rack.Y);
    }

    [Fact]
    public void AddRack_Valid_CreatesSlotsWithSavedRackId()
    {
        // Lưu kệ trước rồi mới sinh khung: nếu làm ngược, mọi khung có RackId = 0.
        var (service, store) = WithRoom();

        var id = service.AddRack(RackReq("K1", 0, 0)).Value!.Id;

        var slots = store.GetSlotsOfRack(id);
        Assert.NotEqual(0, id);
        Assert.Equal(12, slots.Count);
        Assert.All(slots, s => Assert.Equal(id, s.RackId));
        Assert.Equal("K1-T1-R1", slots[0].Code);
        Assert.Empty(store.GetSlotsOfRack(0));
    }

    [Fact]
    public void AddRack_SameCodeOnTopOfExisting_ReturnsConflictNotValid()
    {
        // PlacementValidator bỏ qua kệ cùng mã, nên nếu không chặn trùng mã TRƯỚC,
        // kệ "k1" đặt đè đúng lên K1 sẽ được coi là hợp lệ.
        var (service, store) = WithRoom();
        service.AddRack(RackReq("K1", 0, 0));

        var result = service.AddRack(RackReq("k1", 0, 0));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Đã có kệ mã k1.", result.Message);
        Assert.Single(store.GetRoom()!.Racks);
        Assert.Equal(12, store.GetAllSlots().Count);
    }

    [Fact]
    public void AddRack_OnColumn_ReturnsInvalidAndSavesNothing()
    {
        var (service, store) = WithRoom();
        AddObstacle(store, ObstacleKind.Column, 1.5, 0.3, 0.4, 0.4);

        var result = service.AddRack(RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Kệ đè cột tại (1.5; 0.3) m.", result.Message);
        Assert.Empty(store.GetRoom()!.Racks);
        Assert.Empty(store.GetAllSlots());
    }

    [Fact]
    public void AddRack_OverlapsOtherRack_ReturnsInvalid()
    {
        var (service, store) = WithRoom();
        service.AddRack(RackReq("K1", 0, 0));

        var result = service.AddRack(RackReq("K2", 1.5, 0.5));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Kệ đè lên K1.", result.Message);
        Assert.Single(store.GetRoom()!.Racks);
    }

    [Fact]
    public void AddRack_BadInput_ReturnsInvalidInsteadOfThrowing()
    {
        var (service, store) = WithRoom();

        var result = service.AddRack(RackReq("K1", 0, 0, rotation: 45));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(store.GetRoom()!.Racks);
    }

    [Fact]
    public void AddRack_TwoValidRacks_GetDifferentIdsAndOwnSlots()
    {
        // K1 y 0 → 1; K2 ở y 2,0: cách đúng 1,0 m.
        var (service, store) = WithRoom();

        var k1 = service.AddRack(RackReq("K1", 0, 0)).Value!.Id;
        var k2 = service.AddRack(RackReq("K2", 0, 2.0)).Value!.Id;

        Assert.NotEqual(k1, k2);
        Assert.Equal(12, store.GetSlotsOfRack(k1).Count);
        Assert.Equal(12, store.GetSlotsOfRack(k2).Count);
        Assert.Equal(24, store.GetAllSlots().Count);
    }

    // ---- transaction ----

    [Fact]
    public void AddRack_SavesRackAndSlotsInsideOneTransaction()
    {
        // Store RAM không rollback nên chỉ nhìn dữ liệu thì không thấy được service có bọc transaction không;
        // store gián điệp ghi lại từng lần ghi nằm trong hay ngoài transaction.
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        service.CreateRoom(RoomReq());
        store.Writes.Clear();

        service.AddRack(RackReq("K1", 0, 0));

        Assert.Equal(
            new[] { "SaveRoom (trong transaction)", "AddSlots (trong transaction)" },
            store.Writes);
    }

    [Fact]
    public void AddRack_Rejected_WritesNothing()
    {
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        service.CreateRoom(RoomReq());
        service.AddRack(RackReq("K1", 0, 0));
        store.Writes.Clear();

        service.AddRack(RackReq("K2", 1.0, 0));   // đè K1

        Assert.Empty(store.Writes);
    }
}
