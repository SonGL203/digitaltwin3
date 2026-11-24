using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;

namespace Warehouse.Api.Controllers
{
    [ApiController]
    [Route("api/room3d")]
    public class Room3DController(IRoom3DService room3DService) : ControllerBase
    {
        [HttpGet("{roomId:int}")]
        public IActionResult GetRoom3D(int roomId)
        {
            var room = room3DService.GetRoom3D(roomId);

            if (room == null)
                return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

            return Ok(room);
        }

        [HttpGet("{roomId:int}/slots")]
        public IActionResult GetSlots(int roomId)
        {
            var slots = room3DService.GetSlots(roomId);

            if (slots == null)
                return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

            return Ok(slots);
        }
    }
}
