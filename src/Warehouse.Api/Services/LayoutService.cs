namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

public interface ILayoutService
{
    /// <summary>Kiểm tra thử bản nháp, không ghi gì. null = không có phòng.</summary>
    LayoutCheckViewModel? Validate(int roomId, SaveLayoutRequest request);

    /// <summary>
    /// Lưu bản nháp (gọi sau khi Validate báo hợp lệ). Trả null khi đã lưu;
    /// trả câu lý do khi xung đột với dữ liệu đang có: có người lưu trước, kệ còn chứa pallet, kệ đã bị xoá.
    /// </summary>
    string? Save(int roomId, SaveLayoutRequest request);
}

public class LayoutService(IWarehouseStore store, ILayoutStore layoutStore) : ILayoutService
{
    public LayoutCheckViewModel? Validate(int roomId, SaveLayoutRequest request)
    {
        if (!layoutStore.RoomExists(roomId)) return null;
        var (room, kindError) = ToRoom(roomId, request);
        var check = Check(room, kindError);

        return new LayoutCheckViewModel(
            check.IsValid,
            check.Reason,
            check.Racks.Select(c =>
            {
                var box = c.Rack.GetBounds();
                return new RackCheckViewModel(c.Rack.Code, c.Reason == null, c.Reason, Math.Round(box.Width, 6), Math.Round(box.Depth, 6));
            }).ToList());
    }

    private const string VersionConflict = "Kho vừa được lưu ở nơi khác. Bấm \"Bỏ thay đổi\" để tải bản mới rồi sửa lại.";

    public string? Save(int roomId, SaveLayoutRequest request)
    {
        var current = store.GetRoom(roomId);
        if (current == null) return $"Không có phòng Id {roomId}.";
        if (current.Version != request.Version) return VersionConflict;

        var (room, _) = ToRoom(roomId, request);

        // Kệ/vật cản gửi lên phải còn trong kho
        var oldRacks = current.Racks.ToDictionary(r => r.Id);
        var oldObstacleIds = current.Obstacles.Select(o => o.Id).ToHashSet();
        var missing = room.Racks.FirstOrDefault(r => r.Id != 0 && !oldRacks.ContainsKey(r.Id));
        if (missing != null) return $"Kệ {missing.Code} không còn trong kho (có thể đã bị xoá). Tải lại trang.";
        if (room.Obstacles.Any(o => o.Id != 0 && !oldObstacleIds.Contains(o.Id)))
            return "Có tường/cột không còn trong kho (có thể đã bị xoá). Tải lại trang.";

        // Kệ đang chứa pallet: không xoá, không đổi tầng/ô/kích thước (dời chỗ, xoay thì được)
        foreach (var (rackId, pallets) in layoutStore.CountPalletsPerRack(roomId))
        {
            if (pallets == 0 || !oldRacks.TryGetValue(rackId, out var old)) continue;
            var now = room.Racks.FirstOrDefault(r => r.Id == rackId);
            if (now == null)
                return $"{old.Code} đang chứa {pallets} pallet: lấy hàng ra trước khi xoá kệ.";
            if (now.SlotsDifferFrom(old) || now.LevelValuesDifferFrom(old) || now.SizeDiffersFrom(old))
                return $"{old.Code} đang chứa {pallets} pallet: lấy hàng ra trước khi đổi tầng, ô hoặc kích thước kệ.";
        }

        // Store so version lần nữa trong transaction: phòng khi có người lưu xen giữa lúc kiểm tra và lúc ghi
        return layoutStore.SaveLayout(room, current.Racks, request.Version) ? null : VersionConflict;
    }

    /// <summary>Luật đặt kệ (hàm thuần) + những gì phải hỏi database: trùng tên kho, loại hàng có tồn tại.</summary>
    private LayoutCheck Check(Room room, string? kindError)
    {
        if (kindError != null) return new LayoutCheck(kindError, room.Racks.Select(r => new RackCheck(r, null)).ToList());

        bool needsProductTypes = room.Racks.Any(r => r.ProductTypeId != null);
        var check = PlacementValidator.CheckLayout(room, needsProductTypes ? layoutStore.GetProductTypeIds() : null);

        if (check.IsValid && layoutStore.RoomNameExists(room.Name, room.Id))
            return check with { Reason = $"Đã có kho tên {room.Name}." };
        return check;
    }

    /// <summary>Bản nháp → model. Kệ/vật cản mới (Id null) mang Id = 0. Loại vật cản lạ thì trả lỗi.</summary>
    private static (Room Room, string? KindError) ToRoom(int roomId, SaveLayoutRequest request)
    {
        string? kindError = null;
        var room = new Room
        {
            Id = roomId,
            Name = request.Name.Trim(),
            Width = request.Width,
            Depth = request.Depth,
            Height = request.Height,
            GridSize = request.GridSize,
            MinGap = request.MinGap,
            Margin = request.Margin,
            Version = request.Version,
        };

        foreach (var o in request.Obstacles)
        {
            if (!Enum.TryParse<ObstacleKind>(o.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                kindError ??= $"Loại vật cản \"{o.Kind}\" không hợp lệ (chỉ Wall hoặc Column).";
                continue;
            }
            room.Obstacles.Add(new Obstacle
            {
                Id = o.Id ?? 0, Kind = kind, X = o.X, Y = o.Y, Width = o.Width, Depth = o.Depth,
            });
        }

        foreach (var r in request.Racks)
        {
            room.Racks.Add(new Rack
            {
                Id = r.Id ?? 0,
                Code = r.Code.Trim(),
                X = r.X,
                Y = r.Y,
                Rotation = r.Rotation,
                Levels = r.Levels,
                LevelHeight = r.LevelHeight,
                Bays = r.Bays,
                BayWidth = r.BayWidth,
                Depth = r.Depth,
                HasTop = r.HasTop,
                MaxWeight = r.MaxWeight,
                ProductTypeId = r.ProductTypeId,
            });
        }

        return (room, kindError);
    }
}
