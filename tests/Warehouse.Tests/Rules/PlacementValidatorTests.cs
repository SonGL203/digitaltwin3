using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Tests.Rules;

// Phòng 20 cột × 10 hàng, ô 0,5 m, lối đi tối thiểu 1,0 m (= 2 ô).
// Kệ mẫu: 2 ô × 1,0 m dài 2,0 m = 4 ô, sâu 1,0 m = 2 ô → chiếm 4 × 2 ô (xoay 90° thì 2 × 4).
public class PlacementValidatorTests
{
    private static Room MakeRoom(double minGap = 1.0) => new(20, 10, 0.5, minGap, 0.05);

    private static Rack MakeRack(string code, int x, int y, int rotation = 0)
        => new(1, code, x, y, rotation, 2, 1.3, 2, 1.0, 1.0, false, 1000);

    // ---- hợp lệ ----

    [Fact]
    public void Validate_RackInEmptyRoom_ReturnsNull()
    {
        Assert.Null(PlacementValidator.Validate(MakeRoom(), MakeRack("K1", 5, 3)));
    }

    // ---- ngoài sàn ----

    [Theory]
    [InlineData(16, 0, true)]    // cột 16..19: sát mép phải, vừa trong lưới
    [InlineData(17, 0, false)]   // cột 17..20: thò ra ngoài
    [InlineData(0, 8, true)]     // hàng 8..9: sát mép dưới
    [InlineData(0, 9, false)]    // hàng 9..10: thò ra ngoài
    public void Validate_RackAtGridEdge(int x, int y, bool allowed)
    {
        var reason = PlacementValidator.Validate(MakeRoom(), MakeRack("K1", x, y));

        if (allowed)
            Assert.Null(reason);
        else
            Assert.Equal("Kệ nằm ngoài sàn.", reason);
    }

    [Theory]
    [InlineData(18, 6, true)]    // xoay 90°: chiếm 2 cột × 4 hàng, cột 18..19, hàng 6..9
    [InlineData(19, 6, false)]   // cột 19..20: ngoài
    [InlineData(18, 7, false)]   // hàng 7..10: ngoài
    public void Validate_Rotated90RackAtGridEdge_UsesSwappedFootprint(int x, int y, bool allowed)
    {
        var reason = PlacementValidator.Validate(MakeRoom(), MakeRack("K1", x, y, rotation: 90));

        if (allowed)
            Assert.Null(reason);
        else
            Assert.Equal("Kệ nằm ngoài sàn.", reason);
    }

    // ---- đè tường, vật cản ----

    [Fact]
    public void Validate_RackOnWall_ReportsFirstWallCell()
    {
        var room = MakeRoom();
        room.SetCell(6, 3, CellType.Wall);

        Assert.Equal("Kệ đè tường ở ô (6, 3).", PlacementValidator.Validate(room, MakeRack("K1", 5, 3)));
    }

    [Fact]
    public void Validate_RackOnObstacle_ReportsObstacle()
    {
        var room = MakeRoom();
        room.SetCell(7, 4, CellType.Obstacle);

        Assert.Equal("Kệ đè vật cản ở ô (7, 4).", PlacementValidator.Validate(room, MakeRack("K1", 5, 3)));
    }

    [Fact]
    public void Validate_WallNextToRack_IsAllowed()
    {
        var room = MakeRoom();
        room.SetCell(9, 3, CellType.Wall);   // ngay sau cột 8, kệ chiếm cột 5..8

        Assert.Null(PlacementValidator.Validate(room, MakeRack("K1", 5, 3)));
    }

    [Fact]
    public void Validate_WallAndTooCloseToRack_ReportsWallNotAisle()
    {
        var room = MakeRoom();
        room.SetCell(5, 3, CellType.Wall);
        room.Racks.Add(MakeRack("K2", 10, 3));   // chỉ cách 1 ô trống, thiếu lối đi

        Assert.Equal("Kệ đè tường ở ô (5, 3).", PlacementValidator.Validate(room, MakeRack("K1", 5, 3)));
    }

    // ---- đè kệ khác ----

    [Fact]
    public void Validate_OverlapsExistingRack_ReportsOverlap()
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));   // cột 5..8

        Assert.Equal("Kệ đè lên K2.", PlacementValidator.Validate(room, MakeRack("K3", 7, 3)));
    }

    [Fact]
    public void Validate_OverlapWithOneRackAndTooCloseToAnother_ReportsOverlapFirst()
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));    // cột 5..8
        room.Racks.Add(MakeRack("K4", 12, 3));   // cột 12..15, liền sát kệ mới (cột 8..11)

        Assert.Equal("Kệ đè lên K2.", PlacementValidator.Validate(room, MakeRack("K3", 8, 3)));
    }

    // ---- lối đi ----

    [Theory]
    [InlineData(9, 3, false)]    // cột 9 liền kệ K2 (5..8): 0 ô trống
    [InlineData(10, 3, false)]   // 1 ô trống (cột 9) < 2
    [InlineData(11, 3, true)]    // 2 ô trống (cột 9, 10): đủ lối đi
    public void Validate_AisleBesideExistingRack(int x, int y, bool allowed)
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));

        var reason = PlacementValidator.Validate(room, MakeRack("K1", x, y));

        if (allowed)
            Assert.Null(reason);
        else
            Assert.Equal("Thiếu lối đi với K2.", reason);
    }

    [Theory]
    [InlineData(5, 6, false)]    // K2 chiếm hàng 3..4; hàng 5 là 1 ô trống
    [InlineData(5, 7, true)]     // hàng 5, 6: 2 ô trống
    public void Validate_AisleAboveOrBelowExistingRack(int x, int y, bool allowed)
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));

        var reason = PlacementValidator.Validate(room, MakeRack("K1", x, y));

        if (allowed)
            Assert.Null(reason);
        else
            Assert.Equal("Thiếu lối đi với K2.", reason);
    }

    [Fact]
    public void Validate_MinGapZero_AllowsRacksSideBySide_ButStillRejectsOverlap()
    {
        var room = MakeRoom(minGap: 0);
        room.Racks.Add(MakeRack("K2", 5, 3));

        Assert.Null(PlacementValidator.Validate(room, MakeRack("K1", 9, 3)));
        Assert.Equal("Kệ đè lên K2.", PlacementValidator.Validate(room, MakeRack("K1", 8, 3)));
    }

    [Fact]
    public void Validate_MinGapRoundsUpToWholeCells()
    {
        // 0,6 m / 0,5 m = 1,2 ô → cần 2 ô trống
        var room = MakeRoom(minGap: 0.6);
        room.Racks.Add(MakeRack("K2", 5, 3));

        Assert.Equal("Thiếu lối đi với K2.", PlacementValidator.Validate(room, MakeRack("K1", 10, 3)));
        Assert.Null(PlacementValidator.Validate(room, MakeRack("K1", 11, 3)));
    }

    [Fact]
    public void Validate_RackNextToWallWithoutAisle_IsAllowed()
    {
        // lối đi chỉ áp dụng giữa kệ với kệ, không áp dụng với tường
        var room = MakeRoom();
        room.SetCell(9, 3, CellType.Wall);

        Assert.Null(PlacementValidator.Validate(room, MakeRack("K1", 5, 3)));
    }

    // ---- kiểm tra lại chính kệ đã có ----

    [Fact]
    public void Validate_ExistingRackAtSamePlace_DoesNotOverlapItself()
    {
        var room = MakeRoom();
        var k2 = MakeRack("K2", 5, 3);
        room.Racks.Add(k2);

        Assert.Null(PlacementValidator.Validate(room, k2));
    }

    [Fact]
    public void Validate_MovingExistingRack_IgnoresItsOldPosition()
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));

        // cùng mã K2 nhưng đã dời sang (6, 3): vẫn chồng lên vị trí cũ, nhưng không tính là đè kệ khác
        Assert.Null(PlacementValidator.Validate(room, MakeRack("K2", 6, 3)));
    }

    [Fact]
    public void Validate_SameCodeIgnoresCase()
    {
        var room = MakeRoom();
        room.Racks.Add(MakeRack("K2", 5, 3));

        Assert.Null(PlacementValidator.Validate(room, MakeRack("k2", 6, 3)));
    }

    // ---- không sửa dữ liệu, tham số null ----

    [Fact]
    public void Validate_DoesNotChangeRoom()
    {
        var room = MakeRoom();
        room.SetCell(1, 1, CellType.Wall);
        room.Racks.Add(MakeRack("K2", 5, 3));
        string cellsBefore = room.Cells;

        PlacementValidator.Validate(room, MakeRack("K1", 11, 3));

        Assert.Equal(cellsBefore, room.Cells);
        Assert.Single(room.Racks);
    }

    [Fact]
    public void Validate_NullRoom_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => PlacementValidator.Validate(null!, MakeRack("K1", 0, 0)));

        Assert.Equal("room", ex.ParamName);
    }

    [Fact]
    public void Validate_NullRack_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => PlacementValidator.Validate(MakeRoom(), null!));

        Assert.Equal("rack", ex.ParamName);
    }
}
