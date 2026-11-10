using Warehouse.Api.Models;

namespace Warehouse.Tests.Models;

public class RoomTests
{
    [Fact]
    public void Constructor_WithoutCells_FillsEmptyCells()
    {
        var room = new Room(4, 3, 0.5, 1.0, 0.05);

        Assert.Equal(new string('.', 12), room.Cells);
        Assert.Empty(room.Racks);
    }

    [Fact]
    public void Constructor_WithCells_KeepsCells()
    {
        var room = new Room(2, 2, 0.5, 1.0, 0.05, "#.O.");

        Assert.Equal("#.O.", room.Cells);
    }

    [Theory]
    [InlineData(0, 3, 0.5, 1.0, 0.05, "cols")]
    [InlineData(4, 0, 0.5, 1.0, 0.05, "rows")]
    [InlineData(4, 3, 0, 1.0, 0.05, "cellSize")]
    [InlineData(4, 3, 0.5, -1, 0.05, "minGap")]
    [InlineData(4, 3, 0.5, 1.0, -0.01, "margin")]
    public void Constructor_InvalidNumber_Throws(
        int cols, int rows, double cellSize, double minGap, double margin, string badParam)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new Room(cols, rows, cellSize, minGap, margin));

        Assert.Equal(badParam, ex.ParamName);
    }

    [Fact]
    public void Constructor_CellsWrongLength_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new Room(4, 3, 0.5, 1.0, 0.05, "...."));

        Assert.Equal("cells", ex.ParamName);
    }

    [Fact]
    public void Constructor_CellsInvalidCharacter_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new Room(2, 2, 0.5, 1.0, 0.05, "..x."));

        Assert.Equal("cells", ex.ParamName);
    }
}
