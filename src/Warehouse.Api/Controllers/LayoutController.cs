namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>
/// API mặt bằng, lồng theo phòng: kệ, tường, cột đều thuộc một phòng nên đường dẫn có {roomId}.
/// Controller chỉ nhận ViewModel, gọi service, đổi kết quả sang HTTP; không chứa luật đặt kệ.
/// Đầu vào sai [Range]/[Required] bị [ApiController] chặn tự động (400) trước khi vào các hàm dưới.
/// </summary>
[ApiController]
[Route("api/rooms")]
public class LayoutController(ILayoutService layout) : ControllerBase
{
    /// <summary>Danh sách phòng (tên, kích thước, số kệ, số khung).</summary>
    [HttpGet]
    public IActionResult GetRooms() => ToHttp(layout.GetRooms());

    /// <summary>Tạo phòng mới. Tên trùng phòng khác thì 409.</summary>
    [HttpPost]
    public IActionResult CreateRoom(RoomRequest request) => ToHttp(layout.CreateRoom(request));

    /// <summary>Một phòng kèm kệ (đã có footprint, số khung) và tường/cột.</summary>
    [HttpGet("{roomId:int}")]
    public IActionResult GetRoom(int roomId) => ToHttp(layout.GetLayout(roomId));

    /// <summary>Sửa phòng: tên, kích thước, bước lưới, lối đi, khoảng hở. Đổi lưới không làm kệ nào di chuyển.</summary>
    [HttpPut("{roomId:int}")]
    public IActionResult UpdateRoom(int roomId, RoomRequest request) => ToHttp(layout.UpdateRoom(roomId, request));

    /// <summary>Kiểm tra thử đặt kệ: không lưu. Luôn 200 nếu có phòng; xem IsValid và Reason.</summary>
    [HttpPost("{roomId:int}/racks/validate")]
    public IActionResult ValidateRack(int roomId, RackRequest request) => ToHttp(layout.ValidateRack(roomId, request));

    /// <summary>Đặt kệ thật: kiểm tra lại, lưu kệ, sinh và lưu khung.</summary>
    [HttpPost("{roomId:int}/racks")]
    public IActionResult AddRack(int roomId, RackRequest request) => ToHttp(layout.AddRack(roomId, request));

    /// <summary>Sửa cấu hình kệ (không đổi mã): kiểm tra lại, sinh lại khung. Kệ đang có hàng thì 409.</summary>
    [HttpPut("{roomId:int}/racks/{id:int}")]
    public IActionResult UpdateRack(int roomId, int id, RackRequest request)
        => ToHttp(layout.UpdateRack(roomId, id, request));

    /// <summary>Xoá kệ và mọi khung của nó. Kệ đang có hàng thì 409.</summary>
    [HttpDelete("{roomId:int}/racks/{id:int}")]
    public IActionResult RemoveRack(int roomId, int id) => ToHttp(layout.RemoveRack(roomId, id));

    /// <summary>Thêm tường ngăn hoặc cột.</summary>
    [HttpPost("{roomId:int}/obstacles")]
    public IActionResult AddObstacle(int roomId, ObstacleRequest request) => ToHttp(layout.AddObstacle(roomId, request));

    /// <summary>Xoá tường hoặc cột.</summary>
    [HttpDelete("{roomId:int}/obstacles/{id:int}")]
    public IActionResult RemoveObstacle(int roomId, int id) => ToHttp(layout.RemoveObstacle(roomId, id));

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
