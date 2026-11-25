using System.Diagnostics;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Tests.Rules;

// CheckLayout dùng lưới (RackGrid) để chỉ so kệ ở gần. Kết quả phải giống hệt cách so từng cặp (CheckRackPlacement không lưới).
public class PlacementValidatorGridTests
{
    private static Room RandomRoom(Random rnd, int rackCount)
    {
        var room = new Room
        {
            Id = 1, Name = "Kho", Width = 20 + rnd.Next(80), Depth = 20 + rnd.Next(80), Height = 10,
            GridSize = 0.5, MinGap = rnd.NextDouble() * 2, Margin = 0.05,
        };
        for (int i = 0; i < rackCount; i++)
        {
            room.Racks.Add(new Rack
            {
                Id = i + 1, Code = $"K{i + 1}",
                X = Math.Round(rnd.NextDouble() * (room.Width + 4) - 2, 1),
                Y = Math.Round(rnd.NextDouble() * (room.Depth + 4) - 2, 1),
                Rotation = rnd.Next(2) * 90,
                Levels = 1 + rnd.Next(3), LevelHeight = 1.5, Bays = 1 + rnd.Next(6),
                BayWidth = 0.5 + Math.Round(rnd.NextDouble() * 2, 1), Depth = 0.5 + Math.Round(rnd.NextDouble(), 1),
                MaxWeight = 1000,
            });
        }
        if (rnd.Next(2) == 0) room.Obstacles.Add(new Obstacle { Kind = ObstacleKind.Column, X = 5, Y = 5, Width = 0.5, Depth = 0.5 });
        return room;
    }

    [Fact]
    public void GridGivesSameResultsAsPairwise()
    {
        var rnd = new Random(12345);
        for (int round = 0; round < 300; round++)
        {
            var room = RandomRoom(rnd, rnd.Next(1, 120));
            var check = PlacementValidator.CheckLayout(room);

            for (int i = 0; i < room.Racks.Count; i++)
            {
                var rack = room.Racks[i];
                var expected = PlacementValidator.CheckRackValues(rack, room.Margin) ?? PlacementValidator.CheckRackPlacement(room, rack);
                Assert.Equal(expected, check.Racks[i].Reason);
            }
        }
    }

    [Fact]
    public void TenThousandRacks_ValidateQuickly()
    {
        // 10 000 kệ xếp lưới trong phòng 500 × 500 m: so từng cặp là 100 triệu lần (~17 giây), lưới chỉ vài chục nghìn
        var room = new Room { Id = 1, Name = "Kho lớn", Width = 500, Depth = 500, Height = 15, GridSize = 5, MinGap = 1, Margin = 0.05 };
        int id = 0;
        for (int row = 0; row < 100; row++)
            for (int col = 0; col < 100; col++)
            {
                id++;
                room.Racks.Add(new Rack
                {
                    Id = id, Code = $"K{id}", X = col * 4, Y = row * 4, Levels = 10, LevelHeight = 1,
                    Bays = 2, BayWidth = 1.5, Depth = 1, HasTop = true, MaxWeight = 1000,
                });
            }

        var watch = Stopwatch.StartNew();
        var check = PlacementValidator.CheckLayout(room);
        watch.Stop();

        Assert.True(check.IsValid, check.Reason);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"Mất {watch.ElapsedMilliseconds} ms");
    }
}
