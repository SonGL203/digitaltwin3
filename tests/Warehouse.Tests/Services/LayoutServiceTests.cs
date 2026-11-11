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
    private static RoomRequest RoomReq(double width = 10, double depth = 5, string name = "Kho A") => new()
    {
        Name = name, Width = width, Depth = depth, GridSize = 0.5, MinGap = 1.0, Margin = 0.05,
    };

    private static RackRequest RackReq(string code, double x, double y, int rotation = 0) => new()
    {
        Code = code, X = x, Y = y, Rotation = rotation,
        Levels = 3, LevelHeight = 1.2, Bays = 3, BayWidth = 1.0, Depth = 1.0, HasTop = true, MaxWeight = 1000,
    };

    // Chưa có API thêm tường/cột nên test thêm thẳng vào phòng đã lưu rồi lưu lại.
    private static void AddObstacle(IWarehouseStore store, int roomId, ObstacleKind kind, double x, double y, double width, double depth)
    {
        var room = store.GetRoom(roomId)!;
        room.AddObstacle(new Obstacle(room.Id, kind, x, y, width, depth));
        store.SaveRoom(room);
    }

    // Kho đã có phòng trống.
    private static (LayoutService Service, IWarehouseStore Store, int RoomId) WithRoom()
    {
        var store = new InMemoryWarehouseStore();
        var service = new LayoutService(store);
        var created = service.CreateRoom(RoomReq());
        Assert.True(created.IsSuccess);
        return (service, store, created.Value!.Id);
    }

    // ---- nhiều phòng ----

    [Fact]
    public void GetRooms_Empty_ReturnsEmptyList()
    {
        Assert.Empty(new LayoutService(new InMemoryWarehouseStore()).GetRooms().Value!);
    }

    [Fact]
    public void GetRooms_ListsEveryRoomWithRackAndSlotCounts()
    {
        var (service, _, kho) = WithRoom();
        service.AddRack(kho, RackReq("K1", 0, 0));
        service.AddObstacle(kho, new ObstacleRequest { Kind = "Column", X = 8, Y = 4, Width = 0.4, Depth = 0.4 });
        service.CreateRoom(RoomReq(name: "Kho lạnh", width: 8, depth: 6));

        var rooms = service.GetRooms().Value!;

        Assert.Equal(new[] { "Kho A", "Kho lạnh" }, rooms.Select(r => r.Name));
        Assert.Equal((1, 1, 12), (rooms[0].RackCount, rooms[0].ObstacleCount, rooms[0].SlotCount));
        Assert.Equal((0, 0, 0), (rooms[1].RackCount, rooms[1].ObstacleCount, rooms[1].SlotCount));
        Assert.Equal(8, rooms[1].Width);
    }

    [Fact]
    public void CreateRoom_TrimsNameAndShowsItInLayout()
    {
        var store = new InMemoryWarehouseStore();
        var service = new LayoutService(store);

        var id = service.CreateRoom(RoomReq(name: "  Kho B ")).Value!.Id;

        Assert.Equal("Kho B", service.GetLayout(id).Value!.Name);
    }

    [Theory]
    [InlineData("Kho A")]
    [InlineData("kho a")]
    [InlineData("  Kho A  ")]
    public void CreateRoom_DuplicateName_ReturnsConflict(string name)
    {
        var (service, store, _) = WithRoom();

        var result = service.CreateRoom(RoomReq(name: name));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Đã có phòng tên " + name.Trim() + ".", result.Message);
        Assert.Single(store.GetRooms());
    }

    [Fact]
    public void CreateRoom_EmptyName_ReturnsInvalid()
    {
        var store = new InMemoryWarehouseStore();

        var result = new LayoutService(store).CreateRoom(RoomReq(name: "  "));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(store.GetRooms());
    }

    [Fact]
    public void UpdateRoom_KeepOwnName_IsAllowed()
    {
        var (service, _, roomId) = WithRoom();

        Assert.True(service.UpdateRoom(roomId, RoomReq(width: 12)).IsSuccess);
    }

    [Fact]
    public void UpdateRoom_Rename_Works()
    {
        var (service, store, roomId) = WithRoom();

        Assert.True(service.UpdateRoom(roomId, RoomReq(name: "Kho chính")).IsSuccess);
        Assert.Equal("Kho chính", store.GetRoom(roomId)!.Name);
    }

    [Fact]
    public void UpdateRoom_RenameToOtherRoomsName_ReturnsConflict()
    {
        var (service, store, roomId) = WithRoom();
        service.CreateRoom(RoomReq(name: "Kho B"));

        var result = service.UpdateRoom(roomId, RoomReq(name: "KHO B"));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Kho A", store.GetRoom(roomId)!.Name);
    }

    [Fact]
    public void TwoRooms_SameRackCodeAndPosition_AreIndependent()
    {
        // Luật đặt kệ chỉ nhìn kệ trong CÙNG phòng: K1 ở phòng A không chặn K1 cùng chỗ ở phòng B.
        var (service, store, a) = WithRoom();
        var b = service.CreateRoom(RoomReq(name: "Kho B")).Value!.Id;

        var inA = service.AddRack(a, RackReq("K1", 0, 0));
        var inB = service.AddRack(b, RackReq("K1", 0, 0));

        Assert.True(inA.IsSuccess);
        Assert.True(inB.IsSuccess);
        Assert.Single(store.GetRoom(a)!.Racks);
        Assert.Single(store.GetRoom(b)!.Racks);
        Assert.Equal(12, store.GetSlotsOfRack(inB.Value!.Id).Count);
    }

    [Fact]
    public void UpdateRack_RackOfAnotherRoom_ReturnsNotFound()
    {
        var (service, _, a) = WithRoom();
        var b = service.CreateRoom(RoomReq(name: "Kho B")).Value!.Id;
        var rackInA = service.AddRack(a, RackReq("K1", 0, 0)).Value!.Id;

        Assert.Equal(ServiceError.NotFound, service.UpdateRack(b, rackInA, RackReq("K1", 1, 0)).Error);
    }

    [Fact]
    public void UnknownRoom_EveryCall_ReturnsNotFound()
    {
        var (service, _, _) = WithRoom();

        Assert.Equal(ServiceError.NotFound, service.GetLayout(99).Error);
        Assert.Equal("Không có phòng Id 99.", service.GetLayout(99).Message);
        Assert.Equal(ServiceError.NotFound, service.AddRack(99, RackReq("K1", 0, 0)).Error);
        Assert.Equal(ServiceError.NotFound, service.ValidateRack(99, RackReq("K1", 0, 0)).Error);
        Assert.Equal(ServiceError.NotFound, service.UpdateRoom(99, RoomReq(name: "X")).Error);
    }

    // ---- GetLayout ----

    [Fact]
    public void GetLayout_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).GetLayout(1);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void GetLayout_AfterAddRack_ReturnsRackWithFootprintInMetersAndSlotCount()
    {
        var (service, _, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        var rack = Assert.Single(service.GetLayout(roomId).Value!.Racks);

        Assert.Equal("K1", rack.Code);
        Assert.Equal(3.0, rack.FootprintWidth);
        Assert.Equal(1.0, rack.FootprintDepth);
        Assert.Equal(12, rack.SlotCount);
    }

    [Fact]
    public void GetLayout_ReturnsRoomSizesAndObstacles()
    {
        var (service, store, roomId) = WithRoom();
        AddObstacle(store, roomId, ObstacleKind.Column, 6.5, 2.0, 0.4, 0.4);

        var layout = service.GetLayout(roomId).Value!;

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
        var (service, _, roomId) = WithRoom();

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
        Assert.Empty(store.GetRooms());
    }

    // ---- ValidateRack (kiểm tra thử, không lưu) ----

    [Fact]
    public void ValidateRack_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).ValidateRack(1, RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void ValidateRack_FreeSpot_IsValidWithFootprint()
    {
        var (service, _, roomId) = WithRoom();

        var check = service.ValidateRack(roomId, RackReq("K1", 0, 0)).Value!;

        Assert.True(check.IsValid);
        Assert.Null(check.Reason);
        Assert.Equal(3.0, check.FootprintWidth);
        Assert.Equal(1.0, check.FootprintDepth);
    }

    [Fact]
    public void ValidateRack_RotatedRack_SwapsFootprint()
    {
        var (service, _, roomId) = WithRoom();

        var check = service.ValidateRack(roomId, RackReq("K1", 0, 0, rotation: 90)).Value!;

        Assert.Equal(1.0, check.FootprintWidth);
        Assert.Equal(3.0, check.FootprintDepth);
    }

    [Fact]
    public void ValidateRack_OnWall_IsInvalidButStillSuccessAndSavesNothing()
    {
        var (service, store, roomId) = WithRoom();
        AddObstacle(store, roomId, ObstacleKind.Wall, 1.0, 0, 0.2, 5);

        var result = service.ValidateRack(roomId, RackReq("K1", 0, 0));

        // Đặt không được vẫn là câu trả lời bình thường (IsSuccess), chỉ IsValid = false.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsValid);
        Assert.Equal("Kệ đè tường tại (1; 0) m.", result.Value.Reason);
        Assert.Empty(store.GetRoom(roomId)!.Racks);
        Assert.Empty(store.GetAllSlots());
    }

    [Fact]
    public void ValidateRack_BadRotation_IsInvalidInsteadOfThrowing()
    {
        // Gọi thẳng service nên bỏ qua [AllowedValues]; constructor Rack chặn, service đổi thành lý do.
        var (service, _, roomId) = WithRoom();

        var check = service.ValidateRack(roomId, RackReq("K1", 0, 0, rotation: 45)).Value!;

        Assert.False(check.IsValid);
        Assert.NotNull(check.Reason);
    }

    [Fact]
    public void ValidateRack_MovingExistingRack_DoesNotCollideWithItself()
    {
        // K1 ở x 0 → 3. Dời K1 sang x 1 → 4, chồng lên chỗ cũ: phải hợp lệ vì cùng mã.
        var (service, _, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        var check = service.ValidateRack(roomId, RackReq("K1", 1.0, 0)).Value!;

        Assert.True(check.IsValid);
    }

    [Fact]
    public void ValidateRack_NewRackTooClose_ReportsMissingAisle()
    {
        // K1 ở y 0 → 1; K2 ở y 1,5 chỉ cách 0,5 m, cần 1,0 m.
        var (service, _, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        var check = service.ValidateRack(roomId, RackReq("K2", 0, 1.5)).Value!;

        Assert.False(check.IsValid);
        Assert.Equal("Thiếu lối đi với K1.", check.Reason);
    }

    // ---- AddRack (đặt thật) ----

    [Fact]
    public void AddRack_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).AddRack(1, RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void AddRack_Valid_ReturnsIdAndTrimmedCode()
    {
        var (service, store, roomId) = WithRoom();

        var result = service.AddRack(roomId, RackReq("  K1 ", 0, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal("K1", result.Value!.Code);
        Assert.Equal(store.GetRoom(roomId)!.Racks.Single().Id, result.Value.Id);
    }

    [Fact]
    public void AddRack_Valid_KeepsPositionInMeters()
    {
        var (service, store, roomId) = WithRoom();

        service.AddRack(roomId, RackReq("K1", 2.25, 0.75));

        var rack = store.GetRoom(roomId)!.Racks.Single();
        Assert.Equal(2.25, rack.X);
        Assert.Equal(0.75, rack.Y);
    }

    [Fact]
    public void AddRack_Valid_CreatesSlotsWithSavedRackId()
    {
        // Lưu kệ trước rồi mới sinh khung: nếu làm ngược, mọi khung có RackId = 0.
        var (service, store, roomId) = WithRoom();

        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

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
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        var result = service.AddRack(roomId, RackReq("k1", 0, 0));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Đã có kệ mã k1.", result.Message);
        Assert.Single(store.GetRoom(roomId)!.Racks);
        Assert.Equal(12, store.GetAllSlots().Count);
    }

    [Fact]
    public void AddRack_OnColumn_ReturnsInvalidAndSavesNothing()
    {
        var (service, store, roomId) = WithRoom();
        AddObstacle(store, roomId, ObstacleKind.Column, 1.5, 0.3, 0.4, 0.4);

        var result = service.AddRack(roomId, RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Kệ đè cột tại (1.5; 0.3) m.", result.Message);
        Assert.Empty(store.GetRoom(roomId)!.Racks);
        Assert.Empty(store.GetAllSlots());
    }

    [Fact]
    public void AddRack_OverlapsOtherRack_ReturnsInvalid()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        var result = service.AddRack(roomId, RackReq("K2", 1.5, 0.5));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Kệ đè lên K1.", result.Message);
        Assert.Single(store.GetRoom(roomId)!.Racks);
    }

    [Fact]
    public void AddRack_BadInput_ReturnsInvalidInsteadOfThrowing()
    {
        var (service, store, roomId) = WithRoom();

        var result = service.AddRack(roomId, RackReq("K1", 0, 0, rotation: 45));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(store.GetRoom(roomId)!.Racks);
    }

    [Fact]
    public void AddRack_TwoValidRacks_GetDifferentIdsAndOwnSlots()
    {
        // K1 y 0 → 1; K2 ở y 2,0: cách đúng 1,0 m.
        var (service, store, roomId) = WithRoom();

        var k1 = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        var k2 = service.AddRack(roomId, RackReq("K2", 0, 2.0)).Value!.Id;

        Assert.NotEqual(k1, k2);
        Assert.Equal(12, store.GetSlotsOfRack(k1).Count);
        Assert.Equal(12, store.GetSlotsOfRack(k2).Count);
        Assert.Equal(24, store.GetAllSlots().Count);
    }

    // ---- UpdateRoom ----

    [Fact]
    public void UpdateRoom_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).UpdateRoom(1, RoomReq());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void UpdateRoom_ChangeGridOnly_AlwaysAllowedAndRacksStay()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 2.25, 0.75));
        var req = RoomReq();
        req.GridSize = 1.0;

        var result = service.UpdateRoom(roomId, req);

        Assert.True(result.IsSuccess);
        var room = store.GetRoom(roomId)!;
        Assert.Equal(1.0, room.GridSize);
        Assert.Equal(2.25, room.Racks.Single().X);
    }

    [Fact]
    public void UpdateRoom_Grow_IsAllowed()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));

        Assert.True(service.UpdateRoom(roomId, RoomReq(width: 20, depth: 12)).IsSuccess);
        Assert.Equal(20, store.GetRoom(roomId)!.Width);
    }

    [Fact]
    public void UpdateRoom_ShrinkPushesRackOutside_ReturnsInvalidAndKeepsOldSize()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K2", 6.0, 0));   // x 6 → 9

        var result = service.UpdateRoom(roomId, RoomReq(width: 8));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("K2: Kệ nằm ngoài sàn.", result.Message);
        Assert.Equal(10, store.GetRoom(roomId)!.Width);
    }

    [Fact]
    public void UpdateRoom_BiggerMinGap_RejectsWhenRacksTooClose()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));     // y 0 → 1
        service.AddRack(roomId, RackReq("K2", 0, 2.0));   // cách 1,0 m
        var req = RoomReq();
        req.MinGap = 1.5;

        var result = service.UpdateRoom(roomId, req);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.StartsWith("K1: Thiếu lối đi", result.Message);
        Assert.Equal(1.0, store.GetRoom(roomId)!.MinGap);
    }

    [Fact]
    public void UpdateRoom_ShrinkPushesObstacleOutside_ReturnsInvalid()
    {
        var (service, store, roomId) = WithRoom();
        AddObstacle(store, roomId, ObstacleKind.Column, 9.0, 4.0, 0.4, 0.4);

        var result = service.UpdateRoom(roomId, RoomReq(width: 9));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(10, store.GetRoom(roomId)!.Width);
    }

    [Fact]
    public void UpdateRoom_RejectedDraft_DoesNotTouchRealRackList()
    {
        // Phòng nháp dùng chung đối tượng kệ nhưng có danh sách riêng: phòng thật vẫn đúng 1 kệ.
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 6.0, 0));

        service.UpdateRoom(roomId, RoomReq(width: 8));

        Assert.Single(store.GetRoom(roomId)!.Racks);
    }

    // ---- AddObstacle / RemoveObstacle ----

    private static ObstacleRequest ColumnReq(double x, double y) => new()
    {
        Kind = "Column", X = x, Y = y, Width = 0.4, Depth = 0.4,
    };

    [Fact]
    public void AddObstacle_Free_ReturnsIdAndShowsInLayout()
    {
        var (service, _, roomId) = WithRoom();

        var result = service.AddObstacle(roomId, ColumnReq(6.5, 2.0));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(0, result.Value!.Id);
        var obstacle = Assert.Single(service.GetLayout(roomId).Value!.Obstacles);
        Assert.Equal(result.Value.Id, obstacle.Id);
        Assert.Equal("Column", obstacle.Kind);
    }

    [Fact]
    public void AddObstacle_TwoObstacles_GetDifferentIds()
    {
        var (service, _, roomId) = WithRoom();

        var a = service.AddObstacle(roomId, ColumnReq(6.5, 2.0)).Value!.Id;
        var b = service.AddObstacle(roomId, ColumnReq(8.0, 2.0)).Value!.Id;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AddObstacle_OnRack_ReturnsInvalidAndSavesNothing()
    {
        var (service, store, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));   // x 0 → 3, y 0 → 1

        var result = service.AddObstacle(roomId, ColumnReq(1.0, 0.3));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Vật cản đè lên kệ K1.", result.Message);
        Assert.Empty(store.GetRoom(roomId)!.Obstacles);
    }

    [Fact]
    public void AddObstacle_TouchingRack_IsAllowed()
    {
        var (service, _, roomId) = WithRoom();
        service.AddRack(roomId, RackReq("K1", 0, 0));   // cạnh phải ở x 3,0

        Assert.True(service.AddObstacle(roomId, ColumnReq(3.0, 0.3)).IsSuccess);
    }

    [Fact]
    public void AddObstacle_OutsideRoom_ReturnsInvalid()
    {
        var (service, store, roomId) = WithRoom();

        var result = service.AddObstacle(roomId, ColumnReq(9.8, 2.0));   // 9,8 + 0,4 > 10

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(store.GetRoom(roomId)!.Obstacles);
    }

    [Theory]
    [InlineData("Door")]
    [InlineData("5")]      // Enum.TryParse sẽ nhận "5"; service không được nhận
    [InlineData("wall")]
    public void AddObstacle_UnknownKind_ReturnsInvalid(string kind)
    {
        var (service, store, roomId) = WithRoom();
        var req = ColumnReq(6.5, 2.0);
        req.Kind = kind;

        Assert.Equal(ServiceError.Invalid, service.AddObstacle(roomId, req).Error);
        Assert.Empty(store.GetRoom(roomId)!.Obstacles);
    }

    [Fact]
    public void RemoveObstacle_Existing_RemovesItAndRackCanBePlacedThere()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddObstacle(roomId, ColumnReq(1.0, 0.3)).Value!.Id;

        var result = service.RemoveObstacle(roomId, id);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.GetRoom(roomId)!.Obstacles);
        Assert.True(service.AddRack(roomId, RackReq("K1", 0, 0)).IsSuccess);
    }

    [Fact]
    public void RemoveObstacle_Unknown_ReturnsNotFound()
    {
        var (service, _, roomId) = WithRoom();

        Assert.Equal(ServiceError.NotFound, service.RemoveObstacle(roomId, 99).Error);
    }

    // ---- UpdateRack ----

    private static RackRequest RackReqWith(string code, double x, double y, int levels)
    {
        var req = RackReq(code, x, y);
        req.Levels = levels;
        return req;
    }

    [Fact]
    public void UpdateRack_NoRoom_ReturnsNotFound()
    {
        var result = new LayoutService(new InMemoryWarehouseStore()).UpdateRack(1, 1, RackReq("K1", 0, 0));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public void UpdateRack_UnknownId_ReturnsNotFound()
    {
        var (service, _, roomId) = WithRoom();

        Assert.Equal(ServiceError.NotFound, service.UpdateRack(roomId, 99, RackReq("K1", 0, 0)).Error);
    }

    [Fact]
    public void UpdateRack_MoreLevels_RegeneratesSlots()
    {
        // 3 tầng × 3 ô + nóc = 12 khung → 4 tầng × 3 ô + nóc = 15 khung.
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        var result = service.UpdateRack(roomId, id, RackReqWith("K1", 0, 0, levels: 4));

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value!.SlotCount);
        var slots = store.GetSlotsOfRack(id);
        Assert.Equal(15, slots.Count);
        Assert.Contains(slots, s => s.Code == "K1-T4-R1");
        Assert.Equal(15, store.GetAllSlots().Count);   // khung cũ đã bị xoá, không còn 12 + 15
        Assert.Equal(4, store.GetRoom(roomId)!.Racks.Single().Levels);
    }

    [Fact]
    public void UpdateRack_MoveOverlappingOwnOldPlace_IsAllowed()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        Assert.True(service.UpdateRack(roomId, id, RackReq("K1", 1.0, 0)).IsSuccess);
        Assert.Equal(1.0, store.GetRoom(roomId)!.Racks.Single().X);
    }

    [Fact]
    public void UpdateRack_CodeDiffersOnlyInCaseAndSpaces_IsAllowed()
    {
        var (service, _, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        Assert.True(service.UpdateRack(roomId, id, RackReq(" k1 ", 0, 0)).IsSuccess);
    }

    [Fact]
    public void UpdateRack_ChangeCode_ReturnsInvalid()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        var result = service.UpdateRack(roomId, id, RackReq("K9", 0, 0));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("K1", store.GetRoom(roomId)!.Racks.Single().Code);
    }

    [Fact]
    public void UpdateRack_GrowIntoOtherRack_ReturnsInvalidAndKeepsOldConfig()
    {
        var (service, store, roomId) = WithRoom();
        var k1 = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;   // x 0 → 3
        service.AddRack(roomId, RackReq("K2", 4.0, 0));                     // x 4 → 7, cách 1,0 m
        var wider = RackReq("K1", 0, 0);
        wider.Bays = 4;                                             // x 0 → 4: chạm K2, thiếu lối đi

        var result = service.UpdateRack(roomId, k1, wider);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Thiếu lối đi với K2.", result.Message);
        Assert.Equal(3, store.GetRoom(roomId)!.Racks.Single(r => r.Id == k1).Bays);
        Assert.Equal(12, store.GetSlotsOfRack(k1).Count);
    }

    [Fact]
    public void UpdateRack_BadInput_ReturnsInvalidAndKeepsOldConfig()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        var result = service.UpdateRack(roomId, id, RackReq("K1", 0, 0, rotation: 45));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(0, store.GetRoom(roomId)!.Racks.Single().Rotation);
    }

    [Fact]
    public void UpdateRack_HasPallet_ReturnsConflictAndKeepsSlots()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        var size = new PalletSize("A", 0.8, 0.6, 0.5, "#888");
        store.GetSlotsOfRack(id)[0].Place(new Pallet(1, size, "b1-P001"));

        var result = service.UpdateRack(roomId, id, RackReqWith("K1", 0, 0, levels: 4));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal(12, store.GetSlotsOfRack(id).Count);
        Assert.Equal(3, store.GetRoom(roomId)!.Racks.Single().Levels);
    }

    [Fact]
    public void UpdateRack_WritesInsideOneTransaction()
    {
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        var roomId = service.CreateRoom(RoomReq()).Value!.Id;
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        store.Writes.Clear();

        service.UpdateRack(roomId, id, RackReqWith("K1", 0, 0, levels: 4));

        Assert.Equal(
            new[] { "SaveRoom (trong transaction)", "RemoveSlotsOfRack (trong transaction)", "AddSlots (trong transaction)" },
            store.Writes);
    }

    // ---- RemoveRack ----

    [Fact]
    public void RemoveRack_Existing_RemovesRackAndItsSlots()
    {
        var (service, store, roomId) = WithRoom();
        var k1 = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        var k2 = service.AddRack(roomId, RackReq("K2", 0, 2.0)).Value!.Id;

        var result = service.RemoveRack(roomId, k1);

        Assert.True(result.IsSuccess);
        Assert.Equal("K1", result.Value!.Code);
        Assert.Equal("K2", store.GetRoom(roomId)!.Racks.Single().Code);
        Assert.Empty(store.GetSlotsOfRack(k1));
        Assert.Equal(12, store.GetSlotsOfRack(k2).Count);   // kệ khác không bị đụng
    }

    [Fact]
    public void RemoveRack_ThenPlaceAnotherRackOnSameSpot_IsAllowed()
    {
        var (service, _, roomId) = WithRoom();
        var k1 = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;

        service.RemoveRack(roomId, k1);

        Assert.True(service.AddRack(roomId, RackReq("K5", 0, 0)).IsSuccess);
    }

    [Fact]
    public void RemoveRack_HasPallet_ReturnsConflictAndKeepsRack()
    {
        var (service, store, roomId) = WithRoom();
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        var size = new PalletSize("A", 0.8, 0.6, 0.5, "#888");
        store.GetSlotsOfRack(id)[0].Place(new Pallet(1, size, "b1-P001"));

        var result = service.RemoveRack(roomId, id);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Single(store.GetRoom(roomId)!.Racks);
        Assert.Equal(12, store.GetSlotsOfRack(id).Count);
    }

    [Fact]
    public void RemoveRack_UnknownIdOrOtherRoomsRack_ReturnsNotFound()
    {
        var (service, store, a) = WithRoom();
        var b = service.CreateRoom(RoomReq(name: "Kho B")).Value!.Id;
        var rackInA = service.AddRack(a, RackReq("K1", 0, 0)).Value!.Id;

        Assert.Equal(ServiceError.NotFound, service.RemoveRack(a, 99).Error);
        Assert.Equal(ServiceError.NotFound, service.RemoveRack(b, rackInA).Error);
        Assert.Equal(ServiceError.NotFound, service.RemoveRack(99, rackInA).Error);
        Assert.Single(store.GetRoom(a)!.Racks);
    }

    [Fact]
    public void RemoveRack_WritesInsideOneTransaction()
    {
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        var roomId = service.CreateRoom(RoomReq()).Value!.Id;
        var id = service.AddRack(roomId, RackReq("K1", 0, 0)).Value!.Id;
        store.Writes.Clear();

        service.RemoveRack(roomId, id);

        Assert.Equal(
            new[] { "RemoveSlotsOfRack (trong transaction)", "SaveRoom (trong transaction)" },
            store.Writes);
    }

    // ---- transaction ----

    [Fact]
    public void AddRack_SavesRackAndSlotsInsideOneTransaction()
    {
        // Store RAM không rollback nên chỉ nhìn dữ liệu thì không thấy được service có bọc transaction không;
        // store gián điệp ghi lại từng lần ghi nằm trong hay ngoài transaction.
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        var roomId = service.CreateRoom(RoomReq()).Value!.Id;
        store.Writes.Clear();

        service.AddRack(roomId, RackReq("K1", 0, 0));

        Assert.Equal(
            new[] { "SaveRoom (trong transaction)", "AddSlots (trong transaction)" },
            store.Writes);
    }

    [Fact]
    public void AddRack_Rejected_WritesNothing()
    {
        var store = new SpyWarehouseStore();
        var service = new LayoutService(store);
        var roomId = service.CreateRoom(RoomReq()).Value!.Id;
        service.AddRack(roomId, RackReq("K1", 0, 0));
        store.Writes.Clear();

        service.AddRack(roomId, RackReq("K2", 1.0, 0));   // đè K1

        Assert.Empty(store.Writes);
    }
}
