namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

[ApiController]
[Route("api/rooms")]
public class RoomsController(IRoomService roomService) : ControllerBase
{
    [HttpGet]
    public IActionResult ListRooms()
    {
        return Ok(roomService.ListRooms());
    }

    [HttpGet("{roomId:int}")]
    public IActionResult GetRoom(int roomId)
    {
        var room = roomService.GetRoom(roomId);

        if (room == null)
            return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

        return Ok(room);
    }

    [HttpPost]
    public IActionResult CreateRoom(CreateRoomRequest request)
    {
        var error = roomService.CheckNewRoom(request);
        if (error != null)
            return Problem(detail: error, statusCode: 400);

        var created = roomService.CreateRoom(request);
        if (created == null)
            return Problem(detail: $"Đã có kho tên {request.Name.Trim()}.", statusCode: 409);

        return Ok(created);
    }
}
