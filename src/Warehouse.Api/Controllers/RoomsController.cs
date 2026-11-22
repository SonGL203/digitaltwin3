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
}
