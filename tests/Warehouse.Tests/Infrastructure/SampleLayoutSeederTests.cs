using Microsoft.Extensions.Logging.Abstractions;
using Warehouse.Api.Data;
using Warehouse.Api.Infrastructure;
using Warehouse.Api.Services;

namespace Warehouse.Tests.Infrastructure;

// Đọc đúng file sample-layout.json thật (được chép theo khi build): ai sửa file làm kệ đè nhau
// hay thiếu lối đi thì test đỏ ngay, không phải đợi tới lúc chạy demo.
public class SampleLayoutSeederTests
{
    private static readonly string SamplePath =
        Path.Combine(AppContext.BaseDirectory, "SampleData", "sample-layout.json");

    [Fact]
    public void Seed_EmptyStore_CreatesSampleRoomRacksAndSlots()
    {
        var store = new InMemoryWarehouseStore();
        var layout = new LayoutService(store);

        SampleLayoutSeeder.Seed(layout, NullLogger.Instance, SamplePath);

        var room = store.GetRoom()!;
        Assert.Equal(20, room.Width);
        Assert.Equal(12, room.Depth);
        Assert.Equal(5, room.Obstacles.Count);
        Assert.Equal(8, room.Racks.Count);
        Assert.Equal(108, store.GetAllSlots().Count);
        Assert.Equal(90, room.Racks.Single(r => r.Code == "K8").Rotation);
    }

    [Fact]
    public void Seed_RoomAlreadyExists_DoesNothing()
    {
        var store = new InMemoryWarehouseStore();
        var layout = new LayoutService(store);
        SampleLayoutSeeder.Seed(layout, NullLogger.Instance, SamplePath);

        SampleLayoutSeeder.Seed(layout, NullLogger.Instance, SamplePath);

        Assert.Equal(8, store.GetRoom()!.Racks.Count);
        Assert.Equal(108, store.GetAllSlots().Count);
    }
}
