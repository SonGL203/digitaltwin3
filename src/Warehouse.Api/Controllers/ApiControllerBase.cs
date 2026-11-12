namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;

/// <summary>
/// Chỗ duy nhất đổi lỗi nghiệp vụ sang mã HTTP cho mọi controller. Lỗi trả dạng ProblemDetails (chuẩn RFC 9457),
/// câu lý do nằm ở "detail" để frontend hiện thẳng cho người dùng.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult ToHttp<T>(ServiceResult<T> result) => result.Error switch
    {
        ServiceError.None => Ok(result.Value),
        ServiceError.NotFound => Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
        ServiceError.Conflict => Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
    };
}
