namespace Warehouse.Api.Services;

public record ServiceResult<T>(T? Value, int StatusCode, string? Error)
{
    public static ServiceResult<T> Ok(T value) => new(value, 200, null);

    public static ServiceResult<T> Fail(int statusCode, string error) => new(default, statusCode, error);
}
