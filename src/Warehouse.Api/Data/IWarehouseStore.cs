namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

/// <summary>
/// Một cửa duy nhất để đọc và ghi dữ liệu kho. Services chỉ biết interface này, không biết dữ liệu nằm ở đâu
/// (RAM, file JSON hay EF Core). Rules/ và Models/ không được dùng interface này.
/// Hiện mới có phần mặt bằng (phòng, kệ, khung); pallet, lô, type sẽ thêm ở ngày 3.
/// Quy ước: Save/Add gán Id cho đối tượng mới (Id = 0) giống cơ sở dữ liệu thật, nên sau khi lưu thì Id đã có.
/// </summary>
public interface IWarehouseStore
{
    /// <summary>Phòng (kèm danh sách kệ trong Racks). Null nếu chưa có phòng nào.</summary>
    Room? GetRoom();

    /// <summary>Lưu phòng và các kệ trong room.Racks; đối tượng mới được gán Id.</summary>
    void SaveRoom(Room room);

    IReadOnlyList<Slot> GetAllSlots();

    IReadOnlyList<Slot> GetSlotsOfRack(int rackId);

    /// <summary>Thêm khung mới (thường là kết quả của Rack.CreateSlots()); khung được gán Id.</summary>
    void AddSlots(IEnumerable<Slot> slots);

    void RemoveSlotsOfRack(int rackId);

    /// <summary>
    /// Chạy nhiều lần ghi như MỘT thao tác: hoặc tất cả cùng được lưu, hoặc không lần nào được lưu.
    /// Nếu work ném lỗi giữa chừng, mọi thứ đã ghi bên trong bị huỷ (rollback) và lỗi được ném tiếp ra ngoài.
    /// Store chỉ lo "gom lại", không biết bên trong ghi gì: quyết định ghi gì, theo thứ tự nào vẫn là việc của service.
    /// </summary>
    void InTransaction(Action work);
}
