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

    public IReadOnlyDictionary<int, int> CountSlotsPerRack(int roomId)
    {
        var counts = new Dictionary<int, int>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        // Mỗi dòng Levels = một tầng có Bays khung: cộng Bays theo kệ. Đọc ~vài chục nghìn dòng Levels thay vì cả triệu dòng Slots.
        using var cmd = new NpgsqlCommand(
            @"SELECT l.""RackId"", SUM(k.""Bays"")
              FROM ""Levels"" l
              JOIN ""Racks"" k ON k.""Id"" = l.""RackId""
              WHERE k.""RoomId"" = @room
              GROUP BY l.""RackId""", conn);
        cmd.Parameters.AddWithValue("room", roomId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) counts[reader.GetInt32(0)] = (int)reader.GetInt64(1);
        return counts;
    }

    public IReadOnlyList<RoomSummary> GetRoomSummaries()
    {
        var rooms = new List<RoomSummary>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        // Đếm gom một lần cho mọi phòng (GROUP BY) thay vì mỗi phòng một subquery quét bảng Slots
        using var cmd = new NpgsqlCommand(
            @"SELECT r.""Id"", r.""Name"", r.""Width"", r.""Depth"", r.""Height"", r.""GridSize"",
                     COALESCE(k.rack_count, 0), COALESCE(s.slot_count, 0), COALESCE(o.obstacle_count, 0)
              FROM ""Rooms"" r
              LEFT JOIN (SELECT ""RoomId"", COUNT(*) AS rack_count FROM ""Racks"" GROUP BY ""RoomId"") k
                     ON k.""RoomId"" = r.""Id""
              LEFT JOIN (SELECT k.""RoomId"", SUM(k.""Bays"") AS slot_count
                         FROM ""Levels"" l JOIN ""Racks"" k ON k.""Id"" = l.""RackId""
                         GROUP BY k.""RoomId"") s
                     ON s.""RoomId"" = r.""Id""
              LEFT JOIN (SELECT ""RoomId"", COUNT(*) AS obstacle_count FROM ""Obstacles"" GROUP BY ""RoomId"") o
                     ON o.""RoomId"" = r.""Id""
              ORDER BY r.""Id""", conn);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rooms.Add(new RoomSummary(
                reader.GetInt32(0), reader.GetString(1),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                (int)reader.GetInt64(6), (int)reader.GetInt64(7), (int)reader.GetInt64(8)));
        }
        return rooms;
    }

    public bool SaveLayout(Room room, IReadOnlyList<Rack> oldRacks, int expectedVersion,
        Action<int, int>? onProgress = null, CancellationToken cancel = default)
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

        // Tiến độ: số kệ phải sinh tầng, khung (kệ mới + kệ cũ đổi mã/số tầng/số ô/nóc)
        int total = room.Racks.Count(r => r.Id == 0 || (oldById.TryGetValue(r.Id, out var o) && r.SlotsDifferFrom(o)));
        int built = 0;
        onProgress?.Invoke(built, total);

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
        var rebuild = new List<Rack>();
        foreach (var rack in room.Racks.Where(r => r.Id != 0))
        {
            if (!oldById.TryGetValue(rack.Id, out var old)) continue;   // không thuộc phòng này: bỏ qua

            // Dòng Racks không đổi thì không UPDATE: kho cả nghìn kệ mà dời một kệ không phải chạy cả nghìn câu
            if (!RackRowEquals(rack, old))
            {
                using var cmd = new NpgsqlCommand(
                    @"UPDATE ""Racks"" SET ""Code"" = @code, ""X"" = @x, ""Y"" = @y, ""Rotation"" = @rotation,
                             ""Bays"" = @bays, ""BayWidth"" = @bayWidth, ""Depth"" = @depth, ""ProductTypeId"" = @productTypeId
                      WHERE ""Id"" = @id AND ""RoomId"" = @room", conn, tx);
                AddRackParameters(cmd, rack, room.Id);
                cmd.Parameters.AddWithValue("id", rack.Id);
                cmd.ExecuteNonQuery();
            }

            if (rack.SlotsDifferFrom(old))
            {
                // Đổi mã, số tầng, số ô, nóc: sinh lại toàn bộ tầng và khung (gom lại làm một lượt ở dưới)
                rebuild.Add(rack);
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

        // Kệ cũ phải sinh lại: xoá tầng cũ (khung xoá theo CASCADE) một câu cho tất cả, rồi sinh lại theo lô
        if (rebuild.Count > 0)
        {
            using (var cmd = new NpgsqlCommand(@"DELETE FROM ""Levels"" WHERE ""RackId"" = ANY(@ids)", conn, tx))
            {
                cmd.Parameters.AddWithValue("ids", rebuild.Select(r => r.Id).ToArray());
                cmd.ExecuteNonQuery();
            }
            foreach (var chunk in rebuild.Chunk(BuildChunkSize))
            {
                cancel.ThrowIfCancellationRequested();
                CreateLevelsAndSlots(conn, tx, chunk);
                built += chunk.Length;
                onProgress?.Invoke(built, total);
            }
        }

        // 5. Kệ mới: mỗi lô 1 câu INSERT Racks + 1 câu sinh tầng, khung (thay vì mỗi kệ, mỗi tầng một câu)
        foreach (var chunk in room.Racks.Where(r => r.Id == 0).Chunk(BuildChunkSize))
        {
            cancel.ThrowIfCancellationRequested();
            InsertRacks(conn, tx, chunk, room.Id);
            CreateLevelsAndSlots(conn, tx, chunk);
            built += chunk.Length;
            onProgress?.Invoke(built, total);
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

    /// <summary>Các cột của bảng Racks giống nhau (mã, vị trí, xoay, số ô, rộng ô, sâu, loại hàng).</summary>
    private static bool RackRowEquals(Rack a, Rack b) =>
        a.Code == b.Code && a.Rotation == b.Rotation && a.Bays == b.Bays && a.ProductTypeId == b.ProductTypeId
        && Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9
        && !a.SizeDiffersFrom(b);

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

    /// <summary>Số kệ mỗi lô khi thêm kệ / sinh tầng, khung: 1 000 kệ × ~10 tầng × ~10 ô ≈ 100 000 khung mỗi câu.</summary>
    private const int BuildChunkSize = 1000;

    /// <summary>Thêm một lô kệ mới bằng một câu INSERT (unnest các mảng), gán Id vừa sinh lại cho từng kệ theo mã.</summary>
    private static void InsertRacks(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<Rack> racks, int roomId)
    {
        using var cmd = new NpgsqlCommand(
            @"INSERT INTO ""Racks"" (""RoomId"", ""Code"", ""X"", ""Y"", ""Rotation"", ""Bays"", ""BayWidth"", ""Depth"", ""ProductTypeId"")
              SELECT @room, r.code, r.x, r.y, r.rotation, r.bays, r.bay_width, r.depth, r.product_type_id
              FROM unnest(@codes, @xs, @ys, @rotations, @bays, @bayWidths, @depths, @productTypeIds)
                   AS r(code, x, y, rotation, bays, bay_width, depth, product_type_id)
              RETURNING ""Id"", ""Code""", conn, tx);
        cmd.Parameters.AddWithValue("room", roomId);
        cmd.Parameters.AddWithValue("codes", racks.Select(r => r.Code).ToArray());
        cmd.Parameters.AddWithValue("xs", racks.Select(r => r.X).ToArray());
        cmd.Parameters.AddWithValue("ys", racks.Select(r => r.Y).ToArray());
        cmd.Parameters.AddWithValue("rotations", racks.Select(r => r.Rotation).ToArray());
        cmd.Parameters.AddWithValue("bays", racks.Select(r => r.Bays).ToArray());
        cmd.Parameters.AddWithValue("bayWidths", racks.Select(r => r.BayWidth).ToArray());
        cmd.Parameters.AddWithValue("depths", racks.Select(r => r.Depth).ToArray());
        cmd.Parameters.AddWithValue("productTypeIds", racks.Select(r => r.ProductTypeId).ToArray());

        // Mã kệ không trùng trong phòng (validator đã kiểm tra) nên ghép Id theo mã là chắc chắn
        var byCode = racks.ToDictionary(r => r.Code);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) byCode[reader.GetString(1)].Id = reader.GetInt32(0);
    }

    /// <summary>
    /// Sinh tầng 1..Levels (cùng cao, cùng tải) và tầng nóc (Number = Levels + 1) nếu có, mỗi tầng Bays khung,
    /// cho cả lô kệ trong MỘT câu SQL: CTE thêm Levels (RETURNING Id), câu ngoài thêm Slots theo các tầng vừa sinh.
    /// Mã khung giống Rack.SlotCode: K1-T2-R3, nóc K1-N-R3. Nóc không giới hạn cao theo tầng; Height của nóc chỉ để thoả CHECK > 0.
    /// </summary>
    private static void CreateLevelsAndSlots(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<Rack> racks)
    {
        using var cmd = new NpgsqlCommand(
            @"WITH r AS (
                  SELECT * FROM unnest(@ids, @codes, @levels, @hasTop, @heights, @maxWeights, @bays)
                         AS r(id, code, levels, has_top, height, max_weight, bays)
              ),
              lv AS (
                  INSERT INTO ""Levels"" (""RackId"", ""Number"", ""Height"", ""MaxWeight"", ""IsTop"")
                  SELECT r.id, n, r.height, r.max_weight, n > r.levels
                  FROM r, generate_series(1, r.levels + CASE WHEN r.has_top THEN 1 ELSE 0 END) AS n
                  ORDER BY r.id, n
                  RETURNING ""Id"", ""RackId"", ""Number""
              )
              INSERT INTO ""Slots"" (""LevelId"", ""Position"", ""Code"")
              SELECT lv.""Id"", p,
                     r.code || CASE WHEN lv.""Number"" > r.levels THEN '-N' ELSE '-T' || lv.""Number"" END || '-R' || p
              FROM lv
              JOIN r ON r.id = lv.""RackId""
              CROSS JOIN LATERAL generate_series(1, r.bays) AS p
              ORDER BY lv.""RackId"", lv.""Number"", p", conn, tx);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("ids", racks.Select(r => r.Id).ToArray());
        cmd.Parameters.AddWithValue("codes", racks.Select(r => r.Code).ToArray());
        cmd.Parameters.AddWithValue("levels", racks.Select(r => r.Levels).ToArray());
        cmd.Parameters.AddWithValue("hasTop", racks.Select(r => r.HasTop).ToArray());
        cmd.Parameters.AddWithValue("heights", racks.Select(r => r.LevelHeight).ToArray());
        cmd.Parameters.AddWithValue("maxWeights", racks.Select(r => r.MaxWeight).ToArray());
        cmd.Parameters.AddWithValue("bays", racks.Select(r => r.Bays).ToArray());
        cmd.ExecuteNonQuery();
    }
}
