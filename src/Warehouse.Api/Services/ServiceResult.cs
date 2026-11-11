namespace Warehouse.Api.Services;

/// <summary>Loại lỗi nghiệp vụ. Controller đổi sang mã HTTP; service không biết gì về HTTP.</summary>
public enum ServiceError
{
    None,
    /// <summary>Thiếu dữ liệu cần có (chưa có phòng). → 404</summary>
    NotFound,
    /// <summary>Vi phạm luật (đè tường, thiếu lối đi…). → 400</summary>
    Invalid,
    /// <summary>Trùng với dữ liệu đã có (trùng mã kệ, phòng đã tồn tại). → 409</summary>
    Conflict,
}

/// <summary>
/// Kết quả của một thao tác service: hoặc có Value, hoặc có Error + Message.
/// Dùng thay cho ném exception vì "kệ đè tường" là chuyện bình thường khi người dùng kéo thả, không phải sự cố.
/// </summary>
public record ServiceResult<T>(T? Value, ServiceError Error, string? Message)
{
    public bool IsSuccess => Error == ServiceError.None;

    public static ServiceResult<T> Ok(T value) => new(value, ServiceError.None, null);
    public static ServiceResult<T> Fail(ServiceError error, string message) => new(default, error, message);
}
