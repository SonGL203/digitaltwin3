namespace Warehouse.Api.Data;

using Npgsql;
using Warehouse.Api.Models;

/// <summary>Thao tác PostgreSQL cho trang Mặt bằng. Tách khỏi PostgresWarehouseStore để không đụng code chung.</summary>
public class PostgresLayoutStore(IConfiguration config) : ILayoutStore
{
    private readonly string _connectionString = config.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Warehouse.");

    public bool RoomExists(int id)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"SELECT EXISTS (SELECT 1 FROM ""Rooms"" WHERE ""Id"" = @id)", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (bool)cmd.ExecuteScalar()!;
    }

    public bool RoomNameExists(string name, int exceptRoomId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT EXISTS (SELECT 1 FROM ""Rooms"" WHERE lower(""Name"") = lower(@name) AND ""Id"" <> @except)", conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("except", exceptRoomId);
        return (bool)cmd.ExecuteScalar()!;
    }

    public IReadOnlySet<int> GetProductTypeIds()
    {
        var ids = new HashSet<int>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"SELECT ""Id"" FROM ""ProductTypes""", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    public IReadOnlyDictionary<int, int> CountPalletsPerRack(int roomId)
    {
        var counts = new Dictionary<int, int>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT l.""RackId"", COUNT(s.""PalletId"")
              FROM ""Slots"" s
              JOIN ""Levels"" l ON l.""Id"" = s.""LevelId""
              JOIN ""Racks"" k ON k.""Id"" = l.""RackId""
              WHERE k.""RoomId"" = @room
              GROUP BY l.""RackId""", conn);
        cmd.Parameters.AddWithValue("room", roomId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) counts[reader.GetInt32(0)] = (int)reader.GetInt64(1);
        return counts;
    }

    public bool SaveLayout(Room room, IReadOnlyList<Rack> oldRacks, int expectedVersion)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // 1. Khoá dòng phòng (FOR UPDATE) rồi so version: hai người bấm lưu cùng lúc thì người sau chờ, rồi thấy version đã đổi.
        //    Version khớp nghĩa là oldRacks (service đọc lúc trước) vẫn đúng với database.
        using (var cmd = new NpgsqlCommand(@"SELECT ""Version"" FROM ""Rooms"" WHERE ""Id"" = @id FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", room.Id);
            if (cmd.ExecuteScalar() is not int current || current != expectedVersion) return false;
        }

        var oldById = oldRacks.ToDictionary(r => r.Id);
        var keptRackIds = room.Racks.Where(r => r.Id != 0).Select(r => r.Id).ToArray();
        var keptObstacleIds = room.Obstacles.Where(o => o.Id != 0).Select(o => o.Id).ToArray();

        // 2. Tường, cột: xoá cái bị bỏ, sửa cái cũ, thêm cái mới
        using (var cmd = new NpgsqlCommand(
            @"DELETE FROM ""Obstacles"" WHERE ""RoomId"" = @room AND NOT (""Id"" = ANY(@keep))", conn, tx))
        {
            cmd.Parameters.AddWithValue("room", room.Id);
            cmd.Parameters.AddWithValue("keep", keptObstacleIds);
            cmd.ExecuteNonQuery();
        }
        foreach (var o in room.Obstacles)
        {
            var sql = o.Id == 0
                ? @"INSERT INTO ""Obstacles"" (""RoomId"", ""Kind"", ""X"", ""Y"", ""Width"", ""Depth"")
                    VALUES (@room, @kind, @x, @y, @width, @depth)"
                : @"UPDATE ""Obstacles"" SET ""Kind"" = @kind, ""X"" = @x, ""Y"" = @y, ""Width"" = @width, ""Depth"" = @depth
                    WHERE ""Id"" = @id AND ""RoomId"" = @room";
            using var cmd = new NpgsqlCommand(sql, conn, tx);
            if (o.Id != 0) cmd.Parameters.AddWithValue("id", o.Id);
            cmd.Parameters.AddWithValue("room", room.Id);
            cmd.Parameters.AddWithValue("kind", o.Kind.ToString());
            cmd.Parameters.AddWithValue("x", o.X);
            cmd.Parameters.AddWithValue("y", o.Y);
            cmd.Parameters.AddWithValue("width", o.Width);
            cmd.Parameters.AddWithValue("depth", o.Depth);
            cmd.ExecuteNonQuery();
        }

        // 3. Kệ bị bỏ: xoá trước (tầng, khung xoá theo CASCADE) để kệ mới dùng lại được mã của kệ vừa xoá
        using (var cmd = new NpgsqlCommand(
            @"DELETE FROM ""Racks"" WHERE ""RoomId"" = @room AND NOT (""Id"" = ANY(@keep))", conn, tx))
        {
            cmd.Parameters.AddWithValue("room", room.Id);
            cmd.Parameters.AddWithValue("keep", keptRackIds);
            cmd.ExecuteNonQuery();
        }

        // 4. Kệ cũ: sửa vị trí, kích thước; đổi cấu hình thì sinh lại hoặc sửa tầng
        foreach (var rack in room.Racks.Where(r => r.Id != 0))
        {
            if (!oldById.TryGetValue(rack.Id, out var old)) continue;   // không thuộc phòng này: bỏ qua

            using (var cmd = new NpgsqlCommand(
                @"UPDATE ""Racks"" SET ""Code"" = @code, ""X"" = @x, ""Y"" = @y, ""Rotation"" = @rotation,
                         ""Bays"" = @bays, ""BayWidth"" = @bayWidth, ""Depth"" = @depth, ""ProductTypeId"" = @productTypeId
                  WHERE ""Id"" = @id AND ""RoomId"" = @room", conn, tx))
            {
                AddRackParameters(cmd, rack, room.Id);
                cmd.Parameters.AddWithValue("id", rack.Id);
                cmd.ExecuteNonQuery();
            }

            if (rack.SlotsDifferFrom(old))
            {
                // Đổi mã, số tầng, số ô, nóc: sinh lại toàn bộ tầng và khung
                using (var cmd = new NpgsqlCommand(@"DELETE FROM ""Levels"" WHERE ""RackId"" = @id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", rack.Id);
                    cmd.ExecuteNonQuery();
                }
                CreateLevelsAndSlots(conn, tx, rack);
            }
            else if (rack.LevelValuesDifferFrom(old))
            {
                // Chỉ đổi cao tầng / tải: sửa tại chỗ, khung giữ nguyên Id
                using var cmd = new NpgsqlCommand(
                    @"UPDATE ""Levels"" SET ""Height"" = @height, ""MaxWeight"" = @maxWeight WHERE ""RackId"" = @id", conn, tx);
                cmd.Parameters.AddWithValue("height", rack.LevelHeight);
                cmd.Parameters.AddWithValue("maxWeight", rack.MaxWeight);
                cmd.Parameters.AddWithValue("id", rack.Id);
                cmd.ExecuteNonQuery();
            }
        }

        // 5. Kệ mới: thêm rồi sinh tầng, khung
        foreach (var rack in room.Racks.Where(r => r.Id == 0))
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Racks"" (""RoomId"", ""Code"", ""X"", ""Y"", ""Rotation"", ""Bays"", ""BayWidth"", ""Depth"", ""ProductTypeId"")
                  VALUES (@room, @code, @x, @y, @rotation, @bays, @bayWidth, @depth, @productTypeId)
                  RETURNING ""Id""", conn, tx))
            {
                AddRackParameters(cmd, rack, room.Id);
                rack.Id = (int)cmd.ExecuteScalar()!;
            }
            CreateLevelsAndSlots(conn, tx, rack);
        }

        // 6. Phòng: thông tin mới + tăng version
        using (var cmd = new NpgsqlCommand(
            @"UPDATE ""Rooms"" SET ""Name"" = @name, ""Width"" = @width, ""Depth"" = @depth, ""Height"" = @height,
                     ""GridSize"" = @gridSize, ""MinGap"" = @minGap, ""Margin"" = @margin, ""Version"" = ""Version"" + 1
              WHERE ""Id"" = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", room.Id);
            cmd.Parameters.AddWithValue("name", room.Name);
            cmd.Parameters.AddWithValue("width", room.Width);
            cmd.Parameters.AddWithValue("depth", room.Depth);
            cmd.Parameters.AddWithValue("height", room.Height);
            cmd.Parameters.AddWithValue("gridSize", room.GridSize);
            cmd.Parameters.AddWithValue("minGap", room.MinGap);
            cmd.Parameters.AddWithValue("margin", room.Margin);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    private static void AddRackParameters(NpgsqlCommand cmd, Rack rack, int roomId)
    {
        cmd.Parameters.AddWithValue("room", roomId);
        cmd.Parameters.AddWithValue("code", rack.Code);
        cmd.Parameters.AddWithValue("x", rack.X);
        cmd.Parameters.AddWithValue("y", rack.Y);
        cmd.Parameters.AddWithValue("rotation", rack.Rotation);
        cmd.Parameters.AddWithValue("bays", rack.Bays);
        cmd.Parameters.AddWithValue("bayWidth", rack.BayWidth);
        cmd.Parameters.AddWithValue("depth", rack.Depth);
        cmd.Parameters.AddWithValue("productTypeId", (object?)rack.ProductTypeId ?? DBNull.Value);
    }

    /// <summary>
    /// Sinh tầng 1..Levels (cùng cao, cùng tải) và tầng nóc (Number = Levels + 1) nếu có, mỗi tầng Bays khung.
    /// Nóc không giới hạn cao theo tầng; cột Height của nóc chỉ để thoả CHECK > 0.
    /// </summary>
    private static void CreateLevelsAndSlots(NpgsqlConnection conn, NpgsqlTransaction tx, Rack rack)
    {
        int levelCount = rack.Levels + (rack.HasTop ? 1 : 0);
        for (int number = 1; number <= levelCount; number++)
        {
            int levelId;
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Levels"" (""RackId"", ""Number"", ""Height"", ""MaxWeight"", ""IsTop"")
                  VALUES (@rack, @number, @height, @maxWeight, @isTop)
                  RETURNING ""Id""", conn, tx))
            {
                cmd.Parameters.AddWithValue("rack", rack.Id);
                cmd.Parameters.AddWithValue("number", number);
                cmd.Parameters.AddWithValue("height", rack.LevelHeight);
                cmd.Parameters.AddWithValue("maxWeight", rack.MaxWeight);
                cmd.Parameters.AddWithValue("isTop", number > rack.Levels);
                levelId = (int)cmd.ExecuteScalar()!;
            }

            // Một câu cho cả tầng: Position 1..Bays, Code = tiền tố + số ô (K1-T2-R + 1, 2, 3…)
            string prefix = rack.SlotCode(number, 1)[..^1];
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Slots"" (""LevelId"", ""Position"", ""Code"")
                  SELECT @level, p, @prefix || p FROM generate_series(1, @bays) AS p", conn, tx))
            {
                cmd.Parameters.AddWithValue("level", levelId);
                cmd.Parameters.AddWithValue("prefix", prefix);
                cmd.Parameters.AddWithValue("bays", rack.Bays);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
