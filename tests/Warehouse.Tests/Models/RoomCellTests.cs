using Warehouse.Api.Models;

namespace Warehouse.Tests.Models;

public class RoomCellTests
{
    // Phòng 3 cột × 2 hàng. Ô (x, y) nằm ở vị trí y * 3 + x:
    //   hàng 0:  #  .  O      (0,0) tường, (1,0) trống, (2,0) vật cản
    //   hàng 1:  .  .  #      (0,1) trống, (1,1) trống, (2,1) tường
    private static Room SmallRoom() => new(3, 2, 0.5, 1.0, 0.05, "#.O..#");

    // ---- IsInside ----

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(3, 0, false)]
    [InlineData(0, 2, false)]
    public void IsInside_ChecksGridBounds(int x, int y, bool expected)
    {
        Assert.Equal(expected, SmallRoom().IsInside(x, y));
    }

    // ---- CellAt ----

    [Fact]
    public void CellAt_ReadsTheRightCharacter_ColumnFirstThenRow()
    {
        var room = SmallRoom();

        Assert.Equal(CellType.Wall, room.CellAt(0, 0));
        Assert.Equal(CellType.Empty, room.CellAt(1, 0));
        Assert.Equal(CellType.Obstacle, room.CellAt(2, 0));
        Assert.Equal(CellType.Empty, room.CellAt(0, 1));
        Assert.Equal(CellType.Wall, room.CellAt(2, 1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, 2)]
    public void CellAt_OutsideRoom_Throws(int x, int y)
    {
        Assert.Throws<ArgumentException>(() => SmallRoom().CellAt(x, y));
    }

    // ---- SetCell ----

    [Fact]
    public void SetCell_Wall_ChangesOnlyThatCell()
    {
        var room = SmallRoom();

        room.SetCell(1, 1, CellType.Wall);

        Assert.Equal(CellType.Wall, room.CellAt(1, 1));
        Assert.Equal("#.O.##", room.Cells);
    }

    [Fact]
    public void SetCell_Empty_ClearsAWall()
    {
        var room = SmallRoom();

        room.SetCell(0, 0, CellType.Empty);

        Assert.Equal(CellType.Empty, room.CellAt(0, 0));
        Assert.Equal("..O..#", room.Cells);
    }

    [Fact]
    public void SetCell_Obstacle_Works()
    {
        var room = SmallRoom();

        room.SetCell(0, 1, CellType.Obstacle);

        Assert.Equal(CellType.Obstacle, room.CellAt(0, 1));
    }

    [Fact]
    public void SetCell_KeepsCellsLengthEqualToColsTimesRows()
    {
        var room = SmallRoom();

        room.SetCell(2, 1, CellType.Empty);
        room.SetCell(0, 0, CellType.Obstacle);

        Assert.Equal(room.Cols * room.Rows, room.Cells.Length);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 1)]
    [InlineData(0, 2)]
    public void SetCell_OutsideRoom_Throws(int x, int y)
    {
        var room = SmallRoom();

        Assert.Throws<ArgumentException>(() => room.SetCell(x, y, CellType.Wall));
        Assert.Equal("#.O..#", room.Cells);
    }

    // ---- IsBlocked ----

    [Theory]
    [InlineData(0, 0, true)]   // tường
    [InlineData(2, 0, true)]   // vật cản
    [InlineData(1, 0, false)]  // trống
    [InlineData(0, 1, false)]
    public void IsBlocked_WallAndObstacleBlock_EmptyDoesNot(int x, int y, bool expected)
    {
        Assert.Equal(expected, SmallRoom().IsBlocked(x, y));
    }

    [Fact]
    public void IsBlocked_OutsideRoom_Throws()
    {
        Assert.Throws<ArgumentException>(() => SmallRoom().IsBlocked(5, 5));
    }

    // ---- kích thước thật ----

    [Fact]
    public void WidthAndDepthMeters_AreColsAndRowsTimesCellSize()
    {
        var room = new Room(40, 30, 0.5, 1.0, 0.05);

        Assert.Equal(20.0, room.WidthMeters);
        Assert.Equal(15.0, room.DepthMeters);
    }
}
