namespace Warehouse.Api.Infrastructure;

using System.Text.Json;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

/// <summary>Nội dung file phòng mẫu: đúng hình dạng JSON mà frontend gửi lên các API mặt bằng.</summary>
public record SampleLayout(RoomRequest Room, ObstacleRequest[] Obstacles, RackRequest[] Racks);

/// <summary>
/// Tạo phòng mẫu cho demo khi database chưa có phòng. Đi qua LayoutService (không ghi thẳng DbContext)
/// để phòng mẫu cũng phải qua đúng các luật như khi người dùng tự vẽ: vật cản trong phòng, không đè kệ,
/// PlacementValidator, sinh khung bằng CreateSlots trong transaction.
/// </summary>
public static class SampleLayoutSeeder
{
    public static void Seed(ILayoutService layout, ILogger logger, string path)
    {
        // Đã có phòng nào thì không đụng tới: seed chạy mỗi lần khởi động nhưng chỉ có tác dụng lần đầu,
        // và không bao giờ ghi đè hay thêm phòng khi người dùng đã có phòng của mình.
        if (layout.GetRooms().Value!.Count > 0)
            return;

        var sample = JsonSerializer.Deserialize<SampleLayout>(File.ReadAllText(path), JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"File phòng mẫu rỗng: {path}");

        var roomId = Check(layout.CreateRoom(sample.Room), "phòng").Id;

        foreach (var obstacle in sample.Obstacles)
            Check(layout.AddObstacle(roomId, obstacle), $"{obstacle.Kind} tại ({obstacle.X}; {obstacle.Y})");

        foreach (var rack in sample.Racks)
            Check(layout.AddRack(roomId, rack), $"kệ {rack.Code}");

        logger.LogInformation("Đã tạo phòng mẫu {Name} {Width} × {Depth} m: {Obstacles} vật cản, {Racks} kệ.",
            sample.Room.Name, sample.Room.Width, sample.Room.Depth, sample.Obstacles.Length, sample.Racks.Length);
    }

    // Lỗi thì dừng ngay lúc khởi động, kèm lý do của service, thay vì chạy tiếp với phòng mẫu thiếu kệ.
    private static T Check<T>(ServiceResult<T> result, string what)
    {
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Không tạo được {what} trong phòng mẫu: {result.Message}");
        return result.Value!;
    }
}
