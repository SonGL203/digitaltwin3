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

        [HttpGet("{roomId:int}/racks")]
        public IActionResult GetRacks(int roomId, double? x1, double? y1, double? x2, double? y2)
        {
            var racks = room3DService.GetRacks(roomId, x1, y1, x2, y2);

            if (racks == null)
                return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

            return Ok(racks);
        }

        [HttpGet("{roomId:int}/slots")]
        public IActionResult GetSlots(int roomId, double? x1, double? y1, double? x2, double? y2)
        {
            var slots = room3DService.GetSlots(roomId, x1, y1, x2, y2);

            if (slots == null)
                return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

            return Ok(slots);
        }
    }
}
