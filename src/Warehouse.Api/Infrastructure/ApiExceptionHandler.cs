namespace Warehouse.Api.Infrastructure;

using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using Warehouse.Api.Services;

/// <summary>
/// Đổi mọi exception thành ProblemDetails có câu tiếng Việt ở "detail" (frontend đọc trường này).
///   - Lỗi nghiệp vụ do service ném (NotFound/Conflict/InvalidRequest) → 404/409/400, câu lỗi lấy từ Message.
///   - Lỗi database (trùng dữ liệu, mất kết nối) → 409/503.
///   - Còn lại → 500, không lộ chi tiết lỗi ra ngoài.
/// </summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = Classify(exception);

        // Lỗi nghiệp vụ là chuyện bình thường (người dùng nhập trùng tên…): chỉ ghi Information, không ghi Error.
        if (status >= 500)
            logger.LogError(exception, "Lỗi không xử lý được ({Status}) tại {Path}", status, httpContext.Request.Path);
        else
            logger.LogInformation("{Status} tại {Path}: {Detail}", status, httpContext.Request.Path, detail);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = detail },
        });
    }

    public static (int Status, string Detail) Classify(Exception exception)
    {
        switch (exception)
        {
            case NotFoundException e: return (StatusCodes.Status404NotFound, e.Message);
            case ConflictException e: return (StatusCodes.Status409Conflict, e.Message);
            case InvalidRequestException e: return (StatusCodes.Status400BadRequest, e.Message);
        }

        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return (StatusCodes.Status409Conflict,
                    "Dữ liệu bị trùng: có thể có người vừa lưu cùng lúc. Hãy tải lại rồi thử lại.");

            if (e is NpgsqlException { IsTransient: true })
                return (StatusCodes.Status503ServiceUnavailable, "Không kết nối được cơ sở dữ liệu, thử lại sau.");
        }

        return (StatusCodes.Status500InternalServerError, "Có lỗi không mong đợi trên máy chủ.");
    }
}
