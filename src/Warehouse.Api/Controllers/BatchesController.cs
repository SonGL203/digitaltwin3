using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Controllers
{
    [ApiController]
    public class BatchesController(IBatchService batchService) : ControllerBase
    {
        [HttpPost("/api/batches")]
        public IActionResult CreateBatch(CreateBatchRequest request)
        {
            var error = batchService.CheckBatch(request) ?? batchService.CheckImportSize(request);
            if (error != null)
                return Problem(detail: error, statusCode: 400);

            return Ok(batchService.CreateBatch(request));
        }

        [HttpPost("/api/rooms/{roomId:int}/batches")]
        public IActionResult CreateAndAllocate(int roomId, CreateBatchRequest request)
        {
            var error = batchService.CheckBatch(request) ?? batchService.CheckImportSize(request);
            if (error != null)
                return Problem(detail: error, statusCode: 400);

            var result = batchService.CreateAndAllocate(roomId, request);
            if (result.Error != null)
                return Problem(detail: result.Error, statusCode: result.StatusCode);

            return StatusCode(202, result.Value);
        }

        [HttpGet("/api/batches/{batchId:int}")]
        public IActionResult GetImportStatus(int batchId)
        {
            var result = batchService.GetImportStatus(batchId);
            if (result.Error != null)
                return Problem(detail: result.Error, statusCode: result.StatusCode);

            return Ok(result.Value);
        }

        [HttpPost("/api/rooms/{roomId:int}/batches/{batchId:int}/allocate")]
        public IActionResult Allocate(int roomId, int batchId)
        {
            var result = batchService.StartAllocate(roomId, batchId);
            if (result.Error != null)
                return Problem(detail: result.Error, statusCode: result.StatusCode);

            return StatusCode(202, result.Value);
        }

        [HttpGet("/api/rooms/{roomId:int}/batches/{batchId:int}/allocation")]
        public IActionResult GetAllocation(int roomId, int batchId)
        {
            var result = batchService.GetAllocation(roomId, batchId);
            if (result.Error != null)
                return Problem(detail: result.Error, statusCode: result.StatusCode);

            return Ok(result.Value);
        }

        [HttpPost("/api/rooms/{roomId:int}/batches/check")]
        public IActionResult CheckInRoom(int roomId, CreateBatchRequest request)
        {
            var error = batchService.CheckBatch(request);
            if (error != null)
                return Problem(detail: error, statusCode: 400);

            var check = batchService.CheckInRoom(roomId, request);
            if (check == null)
                return Problem(detail: $"Không có phòng Id {roomId}.", statusCode: 404);

            return Ok(check);
        }
    }
}
