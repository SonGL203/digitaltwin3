namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

// Nơi lấy và cất dữ liệu. Service chỉ nói chuyện với interface này, không biết dữ liệu nằm ở đâu.
// Hiện có SampleWarehouseStore (dữ liệu gõ cứng). Sau này viết thêm EfWarehouseStore đọc PostgreSQL,
// rồi đổi một dòng trong Program.cs là xong, Service và Controller không phải sửa.
public interface IWarehouseStore
{
    Room? GetRoom(int id);                                                   // null nếu không có

    IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds);   // mọi khung của các kệ này
}
