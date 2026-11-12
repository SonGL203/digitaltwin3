namespace Warehouse.Api.Infrastructure;

using System.Text.Json;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>Nội dung file phòng mẫu: đúng hình dạng JSON của bản nháp mà frontend gửi lên (id bỏ trống = mới).</summary>
public record SampleLayout(RoomRequest Room, LayoutObstacleRequest[] Obstacles, LayoutRackRequest[] Racks);

/// <summary>Nội dung file lô mẫu: mỗi lô là các dòng "số lượng + số đo", đúng hình dạng JSON của màn Nhập hàng.</summary>
public record SampleBatches(SampleBatch[] Batches);

public record SampleBatch(string Name, BatchLineRequest[] Lines);

/// <summary>
/// Tạo dữ liệu demo khi database chưa có phòng nào: một phòng mẫu và (nếu có file) vài lô hàng đã xếp sẵn.
/// Đi qua LayoutService và PalletService (không ghi thẳng DbContext) để dữ liệu mẫu cũng phải qua đúng các luật như
/// khi người dùng tự làm: vật cản trong phòng, không đè kệ, PlacementValidator, sinh khung trong transaction,
/// khớp type pallet bằng PalletSizer, xếp bằng Allocator.
/// </summary>
public static class SampleLayoutSeeder
{
    public static void Seed(
        ILayoutService layout, ILogger logger, string path, IPalletService? pallets = null, string? batchesPath = null)
    {
        // Đã có phòng nào thì không đụng tới: seed chạy mỗi lần khởi động nhưng chỉ có tác dụng lần đầu,
        // và không bao giờ ghi đè hay thêm phòng khi người dùng đã có phòng của mình.
        if (layout.GetRooms().Value!.Count > 0)
            return;

        var sample = JsonSerializer.Deserialize<SampleLayout>(File.ReadAllText(path), JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"File phòng mẫu rỗng: {path}");

        var roomId = Check(layout.CreateRoom(sample.Room), "phòng").Id;

        // Cả phòng mẫu đi qua đúng một lần lưu bản nháp, như khi người dùng bấm Lưu nháp (phòng vừa tạo có Version 0).
        var room = sample.Room;
        var draft = new LayoutRequest
        {
            Name = room.Name, Width = room.Width, Depth = room.Depth,
            GridSize = room.GridSize, MinGap = room.MinGap, Margin = room.Margin,
            Version = 0, Obstacles = [.. sample.Obstacles], Racks = [.. sample.Racks],
        };
        Check(layout.SaveLayout(roomId, draft), "bản nháp phòng");

        logger.LogInformation("Đã tạo phòng mẫu {Name} {Width} × {Depth} m: {Obstacles} vật cản, {Racks} kệ.",
            sample.Room.Name, sample.Room.Width, sample.Room.Depth, sample.Obstacles.Length, sample.Racks.Length);

        if (pallets is not null && batchesPath is not null && File.Exists(batchesPath))
            SeedBatches(pallets, logger, roomId, batchesPath);
    }

    // Mỗi lô: nhập (lưu lô và pallet) rồi xếp vào phòng mẫu, đúng hai bước như màn Nhập hàng.
    private static void SeedBatches(IPalletService pallets, ILogger logger, int roomId, string path)
    {
        var sample = JsonSerializer.Deserialize<SampleBatches>(File.ReadAllText(path), JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"File lô mẫu rỗng: {path}");

        foreach (var batch in sample.Batches)
        {
            var request = new BatchRequest { Lines = [.. batch.Lines] };
            var received = Check(pallets.ReceiveBatch(request), $"lô \"{batch.Name}\"");
            var allocation = Check(pallets.Allocate(roomId, received.BatchId), $"xếp lô \"{batch.Name}\"");

            logger.LogInformation("Đã nhập lô mẫu {Code} ({Name}): xếp {Placed}/{Total} pallet, {Rejected} pallet không có khung.",
                received.Code, batch.Name, allocation.Placements.Count, received.PalletCount, allocation.Rejected.Count);
        }
    }

    // Lỗi thì dừng ngay lúc khởi động, kèm lý do của service, thay vì chạy tiếp với dữ liệu mẫu thiếu.
    private static T Check<T>(ServiceResult<T> result, string what)
    {
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Không tạo được {what} trong dữ liệu mẫu: {result.Message}");
        return result.Value!;
    }
}
