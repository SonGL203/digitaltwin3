namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

[ApiController]
[Route("api/rooms/{roomId:int}/layout")]
public class LayoutController(ILayoutService layoutService) : ControllerBase
{
    /// <summary>Kiểm tra thử cả bản nháp (bóng mờ xanh/đỏ khi kéo kệ). Luôn 200 nếu có phòng; lỗi nằm trong isValid/reason.</summary>
    [HttpPost("validate")]
    public IActionResult Validate(int roomId, SaveLayoutRequest request)
    {
        var check = layoutService.Validate(roomId, request);
        if (check == null)
            return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

        return Ok(check);
    }

    /// <summary>Lưu bản nháp: 204 khi xong, 400 khi sai luật, 409 khi có người lưu trước hoặc kệ còn hàng.</summary>
    [HttpPut]
    public IActionResult Save(int roomId, SaveLayoutRequest request)
    {
        var check = layoutService.Validate(roomId, request);
        if (check == null)
            return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);
        if (!check.IsValid)
            return Problem(detail: check.Reason, statusCode: 400);

        var conflict = layoutService.Save(roomId, request);
        if (conflict != null)
            return Problem(detail: conflict, statusCode: 409);

        return NoContent();
    }
}
