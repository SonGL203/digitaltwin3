namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>
/// Danh sách kho, xem một kho, tạo kho trống. Controller mỏng: chỉ gọi IRoomService.
/// Lỗi nghiệp vụ (404, 409, 400) do service ném exception, ApiExceptionHandler đổi sang mã HTTP.
/// </summary>
[ApiController]
[Route("api/rooms")]
public class RoomsController(IRoomService rooms) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoomSummaryViewModel>> List(CancellationToken ct)
        => rooms.ListAsync(ct);

    [HttpGet("{id:int}")]
    public Task<RoomViewModel> Get(int id, CancellationToken ct)
        => rooms.GetAsync(id, ct);

    [HttpPost]
    public Task<RoomCreatedViewModel> Create(CreateRoomRequest request, CancellationToken ct)
        => rooms.CreateAsync(request, ct);

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
