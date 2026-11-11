namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>
/// API mặt bằng. Controller chỉ nhận ViewModel, gọi service, đổi kết quả sang HTTP; không chứa luật đặt kệ.
/// Đầu vào sai [Range]/[Required] bị [ApiController] chặn tự động (400) trước khi vào các hàm dưới.
/// </summary>
[ApiController]
[Route("api/layout")]
public class LayoutController(ILayoutService layout) : ControllerBase
{
    /// <summary>Phòng kèm danh sách kệ (đã có footprint).</summary>
    [HttpGet]
    public IActionResult Get() => ToHttp(layout.GetLayout());

    /// <summary>Tạo phòng (chỉ một phòng).</summary>
    [HttpPost("room")]
    public IActionResult CreateRoom(RoomRequest request) => ToHttp(layout.CreateRoom(request));

    /// <summary>Kiểm tra thử đặt kệ: không lưu. Luôn 200 nếu có phòng; xem IsValid và Reason.</summary>
    [HttpPost("racks/validate")]
    public IActionResult ValidateRack(RackRequest request) => ToHttp(layout.ValidateRack(request));

    /// <summary>Đặt kệ thật: kiểm tra lại, lưu kệ, sinh và lưu khung.</summary>
    [HttpPost("racks")]
    public IActionResult AddRack(RackRequest request) => ToHttp(layout.AddRack(request));

    // Một chỗ duy nhất đổi lỗi nghiệp vụ sang mã HTTP. Lỗi trả dạng ProblemDetails (chuẩn RFC 9457),
    // câu lý do nằm ở "detail" để frontend hiện thẳng cho người dùng.
    private IActionResult ToHttp<T>(ServiceResult<T> result) => result.Error switch
    {
        ServiceError.None => Ok(result.Value),
        ServiceError.NotFound => Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
        ServiceError.Conflict => Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
    };
}
