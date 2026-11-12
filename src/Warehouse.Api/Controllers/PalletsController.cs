namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>
/// API nhập lô và xếp pallet. Controller chỉ nhận ViewModel, gọi service, đổi kết quả sang HTTP.
/// Lập lô trước, chọn kho sau: kiểm tra, nhập lô, chia kho không gắn phòng; xếp vào một kho, đọc khung, đặt lại thì theo phòng.
/// </summary>
public class PalletsController(IPalletService pallets) : ApiControllerBase
{
    /// <summary>Danh sách loại hàng (ốc vít, bánh xe…).</summary>
    [HttpGet("api/product-types")]
    public IActionResult GetProductTypes() => ToHttp(pallets.GetProductTypes());

    /// <summary>Tạo loại hàng. Mã trùng thì 409.</summary>
    [HttpPost("api/product-types")]
    public IActionResult CreateProductType(ProductTypeRequest request) => ToHttp(pallets.CreateProductType(request));

    /// <summary>Kiểm tra lô với mọi kho: kho nào nhận được bao nhiêu, thiếu bao nhiêu, đề xuất chia kho. Không ghi gì.</summary>
    [HttpPost("api/batches/check")]
    public IActionResult CheckBatch(BatchRequest request) => ToHttp(pallets.CheckBatch(request));

    /// <summary>Nhập lô: lưu lô và pallet (b1-P001…), chưa xếp vào khung.</summary>
    [HttpPost("api/batches")]
    public IActionResult ReceiveBatch(BatchRequest request) => ToHttp(pallets.ReceiveBatch(request));

    /// <summary>Xếp các pallet chưa có khung của lô vào phòng. Bấm lại thì không xếp đôi.</summary>
    [HttpPost("api/rooms/{roomId:int}/batches/{batchId:int}/allocate")]
    public IActionResult Allocate(int roomId, int batchId) => ToHttp(pallets.Allocate(roomId, batchId));

    /// <summary>Nhập theo đề xuất: chia các pallet chưa có khung của lô ra các kho. Bấm lại không xếp đôi.</summary>
    [HttpPost("api/batches/{batchId:int}/distribute")]
    public IActionResult Distribute(int batchId) => ToHttp(pallets.Distribute(batchId));

    /// <summary>Mọi khung của phòng kèm pallet/type/màu (để vẽ 3D).</summary>
    [HttpGet("api/rooms/{roomId:int}/slots")]
    public IActionResult GetSlots(int roomId) => ToHttp(pallets.GetSlots(roomId));

    /// <summary>Đặt lại: lấy mọi pallet ra khỏi khung của phòng (pallet, lô vẫn còn).</summary>
    [HttpPost("api/rooms/{roomId:int}/placements/reset")]
    public IActionResult ResetPlacements(int roomId) => ToHttp(pallets.ResetPlacements(roomId));
}
