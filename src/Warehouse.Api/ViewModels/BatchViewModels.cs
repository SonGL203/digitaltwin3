namespace Warehouse.Api.ViewModels;

public class BatchLineRequest
{
    public int Quantity { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double? Weight { get; set; }
    public int? ProductTypeId { get; set; }
}

public class CreateBatchRequest
{
    public List<BatchLineRequest> Lines { get; set; } = [];
}

public record BatchCreatedViewModel(int BatchId, string Code, int PalletCount);

public record BatchLineCheckViewModel(
    string TypeCode, string Color, bool IsNewType, int Quantity, double Length, double Width, double Height);

public record BatchCheckViewModel(
    long Total, List<BatchLineCheckViewModel> Lines, long Placeable, long Missing, List<string> Notes);

public record PlacementViewModel(string PalletCode, string TypeCode, string Color, string RackCode, string SlotCode, bool IsTop);

public record RejectionViewModel(string PalletCode, string Reason);

public record AllocationViewModel(int BatchId, List<PlacementViewModel> Placements, List<RejectionViewModel> Rejected);
