namespace Warehouse.Api.Infrastructure;

using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

/// <summary>
/// Bộ bắt lỗi chung: mọi lỗi không lường trước (mất kết nối DB, trùng unique index khi hai người lưu cùng lúc…)
/// được đổi thành ProblemDetails có câu lý do (cùng dạng với lỗi nghiệp vụ), thay vì trang lỗi 500 trần.
/// Không bao giờ đưa exception.Message ra ngoài (có thể lộ chuỗi kết nối, tên bảng); chi tiết chỉ ghi vào log.
/// </summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = Classify(exception);
        logger.LogError(exception, "Lỗi không xử lý được ({Status}) tại {Path}", status, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = detail },
        });
    }

    /// <summary>
    /// Đi dọc chuỗi InnerException (EF bọc lỗi của Npgsql vài lớp):
    /// vi phạm unique → 409 (có người vừa lưu cùng dữ liệu); DB tạm thời không kết nối được → 503; còn lại → 500.
    /// </summary>
    public static (int Status, string Detail) Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return (StatusCodes.Status409Conflict,
                    "Dữ liệu bị trùng (ví dụ mã kệ, mã lô): có người vừa lưu cùng lúc. Hãy tải lại rồi thử lại.");

            if (e is NpgsqlException { IsTransient: true })
                return (StatusCodes.Status503ServiceUnavailable, "Không kết nối được cơ sở dữ liệu, thử lại sau.");
        }

        return (StatusCodes.Status500InternalServerError, "Có lỗi không mong đợi trên máy chủ.");
    }
}
