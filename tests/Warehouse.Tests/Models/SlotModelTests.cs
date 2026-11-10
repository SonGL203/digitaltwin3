using Warehouse.Api.Models;

namespace Warehouse.Tests.Models;

// Chỉ kiểm tra dữ liệu và ràng buộc của model Slot.
// Luật CanHold/Place/Remove sẽ có test riêng trong Rules/SlotTests.cs.
public class SlotModelTests
{
    [Fact]
    public void Constructor_NewSlot_IsEmpty()
    {
        var slot = new Slot(1, " K1-T1-R1 ", 1, 1, false);

        Assert.Equal("K1-T1-R1", slot.Code);
        Assert.Equal(1, slot.Level);
        Assert.Equal(1, slot.Bay);
        Assert.False(slot.IsTop);
        Assert.Null(slot.AssignedSize);
        Assert.Null(slot.PalletId);
    }

    [Theory]
    [InlineData("", 1, 1, "code")]
    [InlineData("K1-T0-R1", 0, 1, "level")]
    [InlineData("K1-T1-R0", 1, 0, "bay")]
    public void Constructor_InvalidValue_Throws(string code, int level, int bay, string badParam)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Slot(1, code, level, bay, false));

        Assert.Equal(badParam, ex.ParamName);
    }
}
