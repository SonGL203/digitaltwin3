namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;

[ApiController]
[Route("api/rooms")]
public class RoomsController(IRoomService roomService) : ControllerBase
{
    // GET /api/rooms/1  →  một kho kèm kệ, tường/cột (JSON)
    [HttpGet("{roomId:int}")]
    public IActionResult GetRoom(int roomId)
    {
        var room = roomService.GetRoom(roomId);

        // Không có kho này: trả 404 kèm câu lý do (giao diện đọc trường "detail" để hiện cho người dùng)
        if (room == null)
            return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

        // Có: ASP.NET tự đổi room thành JSON, mã 200
        return Ok(room);
    }
}
