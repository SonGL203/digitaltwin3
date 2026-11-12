namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>
/// API mặt bằng. Frontend sửa trên bản nháp trong bộ nhớ, nên chỉ cần: đọc, tạo phòng, kiểm tra thử bản nháp, lưu bản nháp.
/// Controller chỉ nhận ViewModel, gọi service, đổi kết quả sang HTTP; không chứa luật đặt kệ.
/// Đầu vào sai [Range]/[Required] bị [ApiController] chặn tự động (400) trước khi vào các hàm dưới.
/// </summary>
[Route("api/rooms")]
public class LayoutController(ILayoutService layout) : ApiControllerBase
{
    /// <summary>Danh sách phòng (tên, kích thước, số kệ, số khung).</summary>
    [HttpGet]
    public IActionResult GetRooms() => ToHttp(layout.GetRooms());

    /// <summary>Tạo phòng mới (trống). Tên trùng phòng khác thì 409.</summary>
    [HttpPost]
    public IActionResult CreateRoom(RoomRequest request) => ToHttp(layout.CreateRoom(request));

    /// <summary>Một phòng kèm kệ (đã có footprint, số khung), tường/cột và Version.</summary>
    [HttpGet("{roomId:int}")]
    public IActionResult GetRoom(int roomId) => ToHttp(layout.GetLayout(roomId));

    /// <summary>Kiểm tra thử cả bản nháp (bóng mờ): không lưu. Luôn 200 nếu có phòng; xem isValid và racks.</summary>
    [HttpPost("{roomId:int}/layout/validate")]
    public IActionResult ValidateLayout(int roomId, LayoutRequest request) => ToHttp(layout.ValidateLayout(roomId, request));

    /// <summary>
    /// Lưu nháp: nhận cả phòng (thông số, tường, cột, kệ), kiểm tra hết rồi lưu trong một transaction.
    /// Lệch Version, kệ có hàng bị xoá/đổi cấu hình, đặt sai chỗ… đều trả lỗi và không lưu gì.
    /// </summary>
    [HttpPut("{roomId:int}/layout")]
    public IActionResult SaveLayout(int roomId, LayoutRequest request) => ToHttp(layout.SaveLayout(roomId, request));
}
