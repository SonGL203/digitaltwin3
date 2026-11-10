using Warehouse.Api.Models;

namespace Warehouse.Tests.Models;

public class RackFootprintTests
{
    private static Rack MakeRack(int bays = 3, double bayWidth = 1.35, double depth = 1.1,
                                 int rotation = 0, int levels = 2, bool hasTop = false)
        => new(1, "K1", 2, 2, rotation, levels, 1.3, bays, bayWidth, depth, hasTop, 1000);

    [Fact]
    public void GetFootprint_Rotation0_LengthAlongColumns()
    {
        // dài 3 × 1,35 = 4,05 m → 8,1 ô → 9; sâu 1,1 m → 2,2 ô → 3
        var footprint = MakeRack().GetFootprint(0.5);

        Assert.Equal(new Footprint(9, 3), footprint);
    }

    [Fact]
    public void GetFootprint_Rotation90_SwapsColsAndRows()
    {
        var footprint = MakeRack(rotation: 90).GetFootprint(0.5);

        Assert.Equal(new Footprint(3, 9), footprint);
    }

    [Fact]
    public void GetFootprint_ExactMultipleOfCell_DoesNotRoundUpExtra()
    {
        // dài 3 × 0,5 = 1,5 m = đúng 3 ô; sâu 0,5 m = đúng 1 ô
        var footprint = MakeRack(bays: 3, bayWidth: 0.5, depth: 0.5).GetFootprint(0.5);

        Assert.Equal(new Footprint(3, 1), footprint);
    }

    [Fact]
    public void GetFootprint_FloatingPointNoise_DoesNotAddExtraCell()
    {
        // 1,1 / 0,1 trong double ra 11,000000000000002; không có sai số thì làm tròn lên thành 12
        var footprint = MakeRack(bays: 1, bayWidth: 1.1, depth: 0.3).GetFootprint(0.1);

        Assert.Equal(new Footprint(11, 3), footprint);
    }

    [Fact]
    public void GetFootprint_SmallerThanOneCell_StillTakesOneCell()
    {
        var footprint = MakeRack(bays: 1, bayWidth: 0.2, depth: 0.2).GetFootprint(0.5);

        Assert.Equal(new Footprint(1, 1), footprint);
    }

    [Fact]
    public void GetFootprint_DoesNotDependOnLevelsOrTop()
    {
        var low = MakeRack(levels: 1, hasTop: false).GetFootprint(0.5);
        var tall = MakeRack(levels: 5, hasTop: true).GetFootprint(0.5);

        Assert.Equal(low, tall);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void GetFootprint_InvalidCellSize_Throws(double cellSize)
    {
        var ex = Assert.Throws<ArgumentException>(() => MakeRack().GetFootprint(cellSize));

        Assert.Equal("cellSize", ex.ParamName);
    }
}
