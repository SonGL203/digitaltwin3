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

    /// <summary>
    /// Lưu bản nháp ở nền: 202 kèm tiến độ khi đã xếp hàng; 400 sai luật, 409 kho đang được lưu.
    /// Hỏi GET .../layout/save tới khi Status là Done (xong), Conflict (có người lưu trước, kệ còn hàng) hoặc Failed.
    /// </summary>
    [HttpPut]
    public IActionResult Save(int roomId, SaveLayoutRequest request)
    {
        var result = layoutService.StartSave(roomId, request);
        if (result.Error != null)
            return Problem(detail: result.Error, statusCode: result.StatusCode);

        return StatusCode(202, result.Value);
    }

    /// <summary>Tiến độ lần Lưu nháp gần nhất của kho.</summary>
    [HttpGet("save")]
    public IActionResult GetSaveStatus(int roomId)
    {
        var result = layoutService.GetSaveStatus(roomId);
        if (result.Error != null)
            return Problem(detail: result.Error, statusCode: result.StatusCode);

        return Ok(result.Value);
    }
}
