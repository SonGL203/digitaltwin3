using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Tests.Rules;

// Khung rộng 1,2 × sâu 0,9; margin 0,05.
// Kệ thấp (cao tầng 1,3, dùng được 1,25): chỉ vừa pallet A (cao 1,0), không vừa B (cao 1,5).
// Kệ cao (cao tầng 1,8, dùng được 1,75): vừa cả A và B.
public class AllocatorTests
{
    private const double Margin = 0.05;

    private static Rack MakeRack(string code, int levels, int bays, double levelHeight,
                                 bool hasTop = false, int? productTypeId = null)
        => new(1, code, 0, 0, 0, levels, levelHeight, bays, 1.2, 0.9, hasTop, 1000, productTypeId);

    private static Rack LowRack(string code = "K1", int levels = 2, int bays = 3, bool hasTop = false, int? productTypeId = null)
        => MakeRack(code, levels, bays, 1.3, hasTop, productTypeId);

    private static Rack HighRack(string code = "K2", int levels = 2, int bays = 2, bool hasTop = false, int? productTypeId = null)
        => MakeRack(code, levels, bays, 1.8, hasTop, productTypeId);

    private static Pallet PalletOf(string code, double height = 1.0, double length = 1.0, double width = 0.8,
                                   int? productTypeId = null)
        => new(1, new PalletSize(height > 1.2 ? "B" : "A", length, width, height, "#888"), code, productTypeId);

    private static List<SlotCandidate> Candidates(params Rack[] racks)
        => racks.SelectMany(r => r.CreateSlots().Select(s => new SlotCandidate(r, s))).ToList();

    private static List<Pallet> Batch(string prefix, int count, double height = 1.0, int? productTypeId = null)
        => Enumerable.Range(1, count).Select(i => PalletOf($"{prefix}{i}", height, productTypeId: productTypeId)).ToList();

    [Fact]
    public void Allocate_SixAAndTwoB_AllPlaced_BNeverInLowRack()
    {
        // kệ cao (vừa B) liệt kê TRƯỚC: nếu xếp theo thứ tự đến, 6 pallet A sẽ chiếm hết chỗ của B
        var candidates = Candidates(HighRack("K2", levels: 2, bays: 2), LowRack("K1", levels: 2, bays: 3));
        var pallets = Batch("A", 6).Concat(Batch("B", 2, height: 1.5)).ToList();

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Empty(result.Rejected);
        Assert.Equal(8, result.Placements.Count);
        Assert.All(
            result.Placements.Where(p => p.Pallet.Code.StartsWith("B")),
            p => Assert.Equal("K2", p.Target.Rack.Code));
    }

    [Fact]
    public void Allocate_EachSlotUsedByAtMostOnePallet()
    {
        var candidates = Candidates(HighRack(), LowRack());
        var pallets = Batch("A", 10);

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Equal(10, result.Placements.Count);
        Assert.Equal(10, result.Placements.Select(p => p.Target.Slot.Code).Distinct().Count());
    }

    [Fact]
    public void Allocate_TallPalletGoesToTop_NormalPalletsDoNotTakeTopSlots()
    {
        // kệ 1 tầng × 2 ô có nóc: 2 khung thường + 2 khung nóc
        var candidates = Candidates(LowRack("K1", levels: 1, bays: 2, hasTop: true));
        var pallets = Batch("N", 2).Concat(new[] { PalletOf("TALL", height: 3.0) }).ToList();

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Empty(result.Rejected);
        Assert.Single(result.ToTop);
        Assert.Equal("TALL", result.ToTop[0].Pallet.Code);
        Assert.All(result.Placements.Where(p => p.Pallet.Code.StartsWith("N")), p => Assert.False(p.IsTop));
    }

    [Fact]
    public void Allocate_TallPalletsBeyondTopSlots_AreRejectedAsFull()
    {
        var candidates = Candidates(LowRack("K1", levels: 1, bays: 2, hasTop: true));
        var pallets = Batch("TALL", 3, height: 3.0);

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Equal(2, result.Placements.Count);
        var rejection = Assert.Single(result.Rejected);
        Assert.Equal(Allocator.FullReason, rejection.Reason);
    }

    [Fact]
    public void Allocate_PalletTooWide_RejectedWithReason()
    {
        var candidates = Candidates(LowRack());
        var tooWide = PalletOf("WIDE", length: 1.5, width: 1.5);

        var result = Allocator.Allocate(new[] { tooWide }, candidates, Margin);

        Assert.Empty(result.Placements);
        var rejection = Assert.Single(result.Rejected);
        Assert.Equal("WIDE", rejection.Pallet.Code);
        Assert.Equal("Pallet rộng hơn khung.", rejection.Reason);
    }

    [Fact]
    public void Allocate_MorePalletsThanSlots_RejectedCountEqualsShortage()
    {
        var candidates = Candidates(LowRack("K1", levels: 2, bays: 3)); // 6 khung
        var pallets = Batch("A", 9);

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Equal(6, result.Placements.Count);
        Assert.Equal(3, result.Rejected.Count);
        Assert.All(result.Rejected, r => Assert.Equal(Allocator.FullReason, r.Reason));
    }

    [Fact]
    public void Allocate_RespectsProductType()
    {
        var screwsRack = LowRack("K1", productTypeId: 1);
        var generalRack = LowRack("K2", productTypeId: null);
        var candidates = Candidates(screwsRack, generalRack);
        var pallets = Batch("S", 2, productTypeId: 1).Concat(Batch("W", 2, productTypeId: 2)).ToList();

        var result = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Empty(result.Rejected);
        Assert.All(
            result.Placements.Where(p => p.Pallet.ProductTypeId == 2),
            p => Assert.Equal("K2", p.Target.Rack.Code));
    }

    [Fact]
    public void Allocate_SameInputTwice_GivesSameResult()
    {
        var candidates = Candidates(HighRack(), LowRack());
        var pallets = Batch("A", 6).Concat(Batch("B", 2, height: 1.5)).ToList();

        var first = Allocator.Allocate(pallets, candidates, Margin);
        var second = Allocator.Allocate(pallets, candidates, Margin);

        Assert.Equal(
            first.Placements.Select(p => (p.Pallet.Code, p.Target.Slot.Code)),
            second.Placements.Select(p => (p.Pallet.Code, p.Target.Slot.Code)));
    }

    [Fact]
    public void Allocate_DoesNotChangeSlots()
    {
        var candidates = Candidates(HighRack(), LowRack());

        Allocator.Allocate(Batch("A", 6), candidates, Margin);

        Assert.All(candidates, c => Assert.True(c.Slot.IsEmpty));
    }

    [Fact]
    public void Allocate_NoPallets_ReturnsEmptyResult()
    {
        var result = Allocator.Allocate(new List<Pallet>(), Candidates(LowRack()), Margin);

        Assert.Empty(result.Placements);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Allocate_NoSlots_RejectsEveryPallet()
    {
        var result = Allocator.Allocate(Batch("A", 2), new List<SlotCandidate>(), Margin);

        Assert.Empty(result.Placements);
        Assert.Equal(2, result.Rejected.Count);
        Assert.All(result.Rejected, r => Assert.Equal(Allocator.NoSlotsReason, r.Reason));
    }

    [Fact]
    public void Allocate_ExistingOccupiedSlots_AreNotUsed()
    {
        var candidates = Candidates(LowRack("K1", levels: 1, bays: 2)); // 2 khung
        candidates[0].Slot.Place(PalletOf("OLD"));

        var result = Allocator.Allocate(Batch("A", 2), candidates, Margin);

        Assert.Single(result.Placements);
        Assert.Equal("K1-T1-R2", result.Placements[0].Target.Slot.Code);
        Assert.Single(result.Rejected);
    }
}
