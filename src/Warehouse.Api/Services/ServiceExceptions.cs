namespace Warehouse.Api.Services;

// Lỗi nghiệp vụ dự đoán được. Service ném ra, ApiExceptionHandler đổi sang mã HTTP:
//   NotFoundException       → 404
//   ConflictException       → 409
//   InvalidRequestException → 400
// Message là câu tiếng Việt hiện thẳng cho người dùng (frontend đọc ở "detail").

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);

public class InvalidRequestException(string message) : Exception(message);
