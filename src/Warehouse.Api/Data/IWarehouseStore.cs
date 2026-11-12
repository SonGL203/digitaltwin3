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
    /// <summary>Mọi phòng (kèm kệ và vật cản), sắp theo Id.</summary>
    IReadOnlyList<Room> GetRooms();

    /// <summary>Phòng theo Id (kèm kệ và vật cản). Null nếu không có.</summary>
    Room? GetRoom(int id);

    /// <summary>Lưu phòng và các kệ trong room.Racks; đối tượng mới được gán Id.</summary>
    void SaveRoom(Room room);

    IReadOnlyList<Slot> GetAllSlots();

    /// <summary>Khung của các kệ đã cho, bằng MỘT lần đọc (không đọc từng kệ một).</summary>
    IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds);

    /// <summary>Thêm khung mới (thường là kết quả của Rack.CreateSlots()); khung được gán Id.</summary>
    void AddSlots(IEnumerable<Slot> slots);

    void RemoveSlotsOfRack(int rackId);

    /// <summary>Ghi lại khung đã sửa (sau Place/Remove). Cần cho DB thật; store RAM đã sửa thẳng đối tượng đang giữ.</summary>
    void UpdateSlots(IEnumerable<Slot> slots);

    // ----- type pallet, loại hàng, lô, pallet (Ngày 3) -----

    /// <summary>Mọi type pallet (A, B, …), sắp theo Id. Đưa cho PalletSizer để khớp theo số đo.</summary>
    IReadOnlyList<PalletSize> GetPalletSizes();

    /// <summary>Lưu type mới; được gán Id (cần có Id trước khi tạo Pallet vì Pallet nhớ SizeId).</summary>
    void AddPalletSize(PalletSize size);

    IReadOnlyList<ProductType> GetProductTypes();

    void AddProductType(ProductType type);

    /// <summary>Lưu lô cùng các pallet trong batch.Pallets; lô và pallet được gán Id, pallet được gán BatchId.</summary>
    void AddBatch(Batch batch);

    /// <summary>Lô theo Id kèm pallet (và Size của từng pallet). Null nếu không có.</summary>
    Batch? GetBatch(int id);

    /// <summary>Số lô đã có, để đặt mã lô kế tiếp (b1, b2…). Mã trùng vẫn bị unique index chặn.</summary>
    int CountBatches();

    /// <summary>Pallet theo Id (kèm Size), sắp theo Id. Id không có thì bỏ qua.</summary>
    IReadOnlyList<Pallet> GetPallets(IReadOnlyCollection<int> ids);

    /// <summary>
    /// Chạy nhiều lần ghi như MỘT thao tác: hoặc tất cả cùng được lưu, hoặc không lần nào được lưu.
    /// Nếu work ném lỗi giữa chừng, mọi thứ đã ghi bên trong bị huỷ (rollback) và lỗi được ném tiếp ra ngoài.
    /// Store chỉ lo "gom lại", không biết bên trong ghi gì: quyết định ghi gì, theo thứ tự nào vẫn là việc của service.
    /// </summary>
    void InTransaction(Action work);
}
