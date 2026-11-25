namespace Warehouse.Api.Data;

using Warehouse.Api.Models;

/// <summary>Các thao tác database riêng cho trang Mặt bằng (kiểm tra và lưu bản nháp).</summary>
public interface ILayoutStore
{
    bool RoomExists(int id);

    /// <summary>Đã có kho KHÁC tên này chưa (không phân biệt hoa thường).</summary>
    bool RoomNameExists(string name, int exceptRoomId);

    IReadOnlySet<int> GetProductTypeIds();

    /// <summary>Số pallet đang nằm trong khung của từng kệ trong phòng: RackId → số pallet (kệ trống không có mặt).</summary>
    IReadOnlyDictionary<int, int> CountPalletsPerRack(int roomId);

    /// <summary>
    /// Số khung của từng kệ trong phòng: RackId → số khung (kệ chưa có tầng không có mặt).
    /// Đếm trên bảng Levels (mỗi tầng luôn có đủ Bays khung, sinh ở CreateLevelsAndSlots), không đọc bảng Slots.
    /// </summary>
    IReadOnlyDictionary<int, int> CountSlotsPerRack(int roomId);

    /// <summary>Danh sách kho cho màn index (số kệ, số khung, số tường/cột). Số khung đếm qua Levels như trên.</summary>
    IReadOnlyList<RoomSummary> GetRoomSummaries();

    /// <summary>
    /// Lưu cả mặt bằng trong một transaction: thông tin phòng, tường/cột, kệ (Id = 0 là kệ mới),
    /// sinh lại tầng và khung cho kệ đổi cấu hình, tăng Version. Không kiểm tra luật (service đã làm).
    /// oldRacks: kệ đang lưu của phòng ở version expectedVersion (để biết kệ nào đổi khung, kệ nào chỉ dời chỗ).
    /// Trả false (không ghi gì) khi không có phòng hoặc Version trong database khác expectedVersion.
    /// onProgress(built, total): gọi sau mỗi kệ sinh xong tầng, khung (total = số kệ mới + kệ đổi cấu hình).
    /// cancel bị huỷ giữa chừng thì ném OperationCanceledException, transaction bị bỏ, không ghi gì.
    /// </summary>
    bool SaveLayout(Room room, IReadOnlyList<Rack> oldRacks, int expectedVersion,
        Action<int, int>? onProgress = null, CancellationToken cancel = default);
}
