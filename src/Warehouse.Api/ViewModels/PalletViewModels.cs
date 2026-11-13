namespace Warehouse.Api.ViewModels;

using System.ComponentModel.DataAnnotations;

// ===== Đầu vào =====
// Số đo bằng mét, khối lượng bằng kg. Giới hạn trên chỉ để chặn gõ nhầm, không phải luật nghiệp vụ.

/// <summary>Một dòng nhập lô: cả Quantity pallet cùng số đo (và khối lượng, loại hàng). Type A/B do server tự khớp/tạo.</summary>
public class BatchLineRequest
{
    [Range(1, 1000, ErrorMessage = "Số lượng phải từ 1 đến 1000 pallet.")] public int Quantity { get; set; }
    [Range(0.01, 10, ErrorMessage = "Chiều dài phải từ 0,01 đến 10 m.")] public double Length { get; set; }
    [Range(0.01, 10, ErrorMessage = "Chiều rộng phải từ 0,01 đến 10 m.")] public double Width { get; set; }
    [Range(0.01, 10, ErrorMessage = "Chiều cao phải từ 0,01 đến 10 m.")] public double Height { get; set; }
    /// <summary>Null = không kiểm tra tải trọng.</summary>
    [Range(0.1, 100000, ErrorMessage = "Khối lượng phải từ 0,1 đến 100000 kg (hoặc để trống).")] public double? Weight { get; set; }
    /// <summary>Null = hàng chưa phân loại (chỉ vào kệ chung).</summary>
    public int? ProductTypeId { get; set; }
}

/// <summary>Cả lô nhập: nhiều dòng (ví dụ 6 pallet cỡ này + 2 pallet cỡ kia). Dùng cho cả xem trước lẫn nhập thật.</summary>
public class BatchRequest
{
    [Required, MinLength(1, ErrorMessage = "Lô phải có ít nhất một dòng."), MaxLength(50, ErrorMessage = "Một lô tối đa 50 dòng.")]
    public List<BatchLineRequest> Lines { get; set; } = [];
}

public class ProductTypeRequest
{
    [Required(ErrorMessage = "Mã loại hàng không được rỗng."), StringLength(20, ErrorMessage = "Mã loại hàng tối đa 20 ký tự.")] public string Code { get; set; } = "";
    [Required(ErrorMessage = "Tên loại hàng không được rỗng."), StringLength(50, ErrorMessage = "Tên loại hàng tối đa 50 ký tự.")] public string Name { get; set; } = "";
}

// ===== Đầu ra =====

public record ProductTypeViewModel(int Id, string Code, string Name);

/// <summary>Một dòng của lô sau khi khớp type (type có sẵn hay mới tạo).</summary>
public record BatchLineCheckViewModel(
    int Quantity, string TypeCode, bool IsNewType, string Color, double Length, double Width, double Height,
    double? Weight, int? ProductTypeId);

/// <summary>
/// Một kho nếu nhận RIÊNG cả lô: xếp được bao nhiêu pallet, thiếu bao nhiêu ô. Notes: lý do theo từng type còn thiếu
/// ("Không có kệ thích hợp cho type B: …", "Các kệ thích hợp cho type A đã đầy.").
/// </summary>
public record RoomCheckViewModel(int RoomId, string RoomName, int Placeable, int Missing, IReadOnlyList<string> Notes);

/// <summary>Kiểm tra lô trước khi nhập: dòng nào thành type nào, từng kho nhận được bao nhiêu, và đề xuất chia kho.</summary>
public record BatchCheckViewModel(
    int Total, IReadOnlyList<BatchLineCheckViewModel> Lines, IReadOnlyList<RoomCheckViewModel> Rooms, DistributionViewModel Plan);

/// <summary>Id và mã do server gán cho lô vừa nhập (pallet có mã b1-P001…, chưa vào khung nào).</summary>
public record BatchReceivedViewModel(int BatchId, string Code, int PalletCount);

/// <summary>Một pallet được xếp vào một khung. Danh sách theo đúng thứ tự đã xếp, dùng cho hiệu ứng bay.</summary>
public record PlacementViewModel(
    string PalletCode, string TypeCode, string Color, string RackCode, string SlotCode, int Level, int Bay, bool IsTop);

public record RejectionViewModel(string PalletCode, string Reason);

/// <summary>AlreadyPlaced: số pallet của lô đã nằm trong khung từ trước (bấm Xếp lần nữa thì không xếp đôi).</summary>
public record AllocationViewModel(
    int BatchId, IReadOnlyList<PlacementViewModel> Placements, IReadOnlyList<RejectionViewModel> Rejected, int AlreadyPlaced);

/// <summary>Phần của lô vào một kho. Placements rỗng khi chỉ là dự kiến (plan).</summary>
public record RoomShareViewModel(int RoomId, string RoomName, int Count, IReadOnlyList<PlacementViewModel> Placements);

/// <summary>Kết quả chia lô ra các kho. BatchId null khi chỉ là dự kiến (chưa nhập lô).</summary>
public record DistributionViewModel(
    int? BatchId, int Total, int Placed, IReadOnlyList<RoomShareViewModel> Rooms, IReadOnlyList<RejectionViewModel> Rejected);

/// <summary>
/// Một khung của phòng; pallet/type/màu/số đo null khi khung trống. Số đo pallet (mét) đã tính chiều đặt trong khung
/// (Slot.PalletFootprint): PalletAlongBay dọc ô, PalletAcrossDepth theo chiều sâu kệ, để 3D vẽ đúng hình pallet.
/// </summary>
public record SlotViewModel(
    int Id, int RackId, string RackCode, string Code, int Level, int Bay, bool IsTop,
    int? PalletId, string? PalletCode, string? TypeCode, string? Color,
    double? PalletAlongBay = null, double? PalletAcrossDepth = null, double? PalletHeight = null);

public record ResetViewModel(int Cleared);
