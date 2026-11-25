using Npgsql;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Api.Data
{
    public class BatchStore(IConfiguration config) : IBatchStore
    {
        private readonly string _connectionString = config.GetConnectionString("Warehouse")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Warehouse.");

        public List<PalletType> GetPalletTypes()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT ""Id"", ""Code"", ""Length"", ""Width"", ""Height"", ""Weight"", ""Color""
                  FROM ""PalletTypes""
                  ORDER BY ""Id""", conn);
            using var reader = cmd.ExecuteReader();

            var types = new List<PalletType>();
            while (reader.Read())
            {
                types.Add(new PalletType
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Length = reader.GetDouble(2),
                    Width = reader.GetDouble(3),
                    Height = reader.GetDouble(4),
                    Weight = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    Color = reader.GetString(6),
                });
            }
            return types;
        }

        public HashSet<int> GetProductTypeIds()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT ""Id"" FROM ""ProductTypes""", conn);
            using var reader = cmd.ExecuteReader();

            var ids = new HashSet<int>();
            while (reader.Read())
                ids.Add(reader.GetInt32(0));
            return ids;
        }

        public bool GetExistRoom(int roomId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT EXISTS (SELECT 1 FROM ""Rooms"" WHERE ""Id"" = @id)", conn);
            cmd.Parameters.AddWithValue("id", roomId);
            return (bool)cmd.ExecuteScalar()!;
        }

        private const string SlotJoins = @"
            FROM ""Slots"" s
            JOIN ""Levels"" l ON l.""Id"" = s.""LevelId""
            JOIN ""Racks""  k ON k.""Id"" = l.""RackId""
            JOIN ""Rooms""  r ON r.""Id"" = k.""RoomId""";

        private const string FitCondition = @"
            WHERE r.""Id"" = @roomId
              AND s.""Active"" = true
              AND s.""PalletId"" IS NULL
              AND (k.""ProductTypeId"" IS NULL OR k.""ProductTypeId"" = @productTypeId)
              AND ((@length <= k.""BayWidth"" + 1e-9 AND @width <= k.""Depth"" + 1e-9)
                OR (@width <= k.""BayWidth"" + 1e-9 AND @length <= k.""Depth"" + 1e-9))
              AND (l.""IsTop"" = true OR @height <= l.""Height"" - r.""Margin"" + 1e-9)
              AND (@weight IS NULL OR @weight <= l.""MaxWeight"" + 1e-9)";

        private const string LevelFitCondition = @"
            WHERE r.""Id"" = @roomId
              AND (k.""ProductTypeId"" IS NULL OR k.""ProductTypeId"" = @productTypeId)
              AND ((@length <= k.""BayWidth"" + 1e-9 AND @width <= k.""Depth"" + 1e-9)
                OR (@width <= k.""BayWidth"" + 1e-9 AND @length <= k.""Depth"" + 1e-9))
              AND (l.""IsTop"" = true OR @height <= l.""Height"" - r.""Margin"" + 1e-9)
              AND (@weight IS NULL OR @weight <= l.""MaxWeight"" + 1e-9)";

        private static void AddFitParameters(NpgsqlCommand cmd, int roomId, double length, double width,
                                             double height, double? weight, int? productTypeId)
        {
            cmd.Parameters.AddWithValue("roomId", roomId);
            cmd.Parameters.AddWithValue("length", length);
            cmd.Parameters.AddWithValue("width", width);
            cmd.Parameters.AddWithValue("height", height);
            cmd.Parameters.Add(new NpgsqlParameter("weight", NpgsqlTypes.NpgsqlDbType.Double) { Value = (object?)weight ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("productTypeId", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)productTypeId ?? DBNull.Value });
        }

        public int CountFreeSlots(int roomId, double length, double width, double height, double? weight, int? productTypeId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT COALESCE(SUM(k.""Bays"" - (
                             SELECT COUNT(*) FROM ""Slots"" s
                             WHERE s.""LevelId"" = l.""Id""
                               AND (s.""PalletId"" IS NOT NULL OR s.""Active"" = false))), 0)
                  FROM ""Levels"" l
                  JOIN ""Racks""  k ON k.""Id"" = l.""RackId""
                  JOIN ""Rooms""  r ON r.""Id"" = k.""RoomId""" + LevelFitCondition, conn);
            AddFitParameters(cmd, roomId, length, width, height, weight, productTypeId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<FreeSlot> GetFreeSlots(int roomId, PalletType type, int? productTypeId, int limit)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT s.""Id"", s.""Code"", k.""Code"", l.""IsTop""" + SlotJoins + FitCondition +
                // Kệ gắn đúng loại hàng của pallet được lấp trước, kệ không gắn loại (dùng chung) để sau
                @" ORDER BY (k.""ProductTypeId"" IS NULL), l.""IsTop"", k.""Id"", l.""Number"", s.""Position""
                   LIMIT @limit", conn);
            AddFitParameters(cmd, roomId, type.Length, type.Width, type.Height, type.Weight, productTypeId);
            cmd.Parameters.AddWithValue("limit", limit);

            using var reader = cmd.ExecuteReader();
            var slots = new List<FreeSlot>();
            while (reader.Read())
                slots.Add(new FreeSlot(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            return slots;
        }

        public int NextBatchNumber()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT COALESCE(MAX(""Id""), 0) + 1 FROM ""Batches""", conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void SaveBatch(Batch batch, List<PalletType> newTypes)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            foreach (var type in newTypes)
            {
                using var cmd = new NpgsqlCommand(
                    @"INSERT INTO ""PalletTypes"" (""Code"", ""Length"", ""Width"", ""Height"", ""Weight"", ""Color"")
                      VALUES (@code, @length, @width, @height, @weight, @color)
                      RETURNING ""Id""", conn, tx);
                cmd.Parameters.AddWithValue("code", type.Code);
                cmd.Parameters.AddWithValue("length", type.Length);
                cmd.Parameters.AddWithValue("width", type.Width);
                cmd.Parameters.AddWithValue("height", type.Height);
                cmd.Parameters.AddWithValue("weight", (object?)type.Weight ?? DBNull.Value);
                cmd.Parameters.AddWithValue("color", type.Color);
                type.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Batches"" (""Code"") VALUES (@code) RETURNING ""Id""", conn, tx))
            {
                cmd.Parameters.AddWithValue("code", batch.Code);
                batch.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }

            foreach (var line in batch.Lines)
            {
                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO ""BatchLines"" (""BatchId"", ""Quantity"", ""ProductTypeId"", ""PalletTypeId"")
                      VALUES (@batchId, @quantity, @productTypeId, @palletTypeId)
                      RETURNING ""Id""", conn, tx))
                {
                    cmd.Parameters.AddWithValue("batchId", batch.Id);
                    cmd.Parameters.AddWithValue("quantity", line.Quantity);
                    cmd.Parameters.AddWithValue("productTypeId", (object?)line.ProductTypeId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("palletTypeId", line.Type.Id);
                    line.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }

            }

            tx.Commit();
        }

        public void InsertPallets(string batchCode, int lineId, int from, int to)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Pallets"" (""Code"", ""BatchLineId"")
                  SELECT @batchCode || '-P' || CASE WHEN n < 1000 THEN lpad(n::text, 3, '0') ELSE n::text END,
                         @lineId
                  FROM generate_series(@from, @to) AS n", conn);
            cmd.Parameters.AddWithValue("batchCode", batchCode);
            cmd.Parameters.AddWithValue("lineId", lineId);
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("to", to);
            cmd.CommandTimeout = 300;
            cmd.ExecuteNonQuery();
        }

        public string? GetBatchCode(int batchId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT ""Code"" FROM ""Batches"" WHERE ""Id"" = @id", conn);
            cmd.Parameters.AddWithValue("id", batchId);
            return cmd.ExecuteScalar() as string;
        }

        public List<LineImport> GetImportProgress(int batchId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT bl.""Id"", bl.""Quantity"",
                         SUM(bl.""Quantity"") OVER (ORDER BY bl.""Id"" ROWS UNBOUNDED PRECEDING) - bl.""Quantity"" + 1,
                         (SELECT COUNT(*) FROM ""Pallets"" p WHERE p.""BatchLineId"" = bl.""Id"")
                  FROM ""BatchLines"" bl
                  WHERE bl.""BatchId"" = @id
                  ORDER BY bl.""Id""", conn);
            cmd.Parameters.AddWithValue("id", batchId);

            using var reader = cmd.ExecuteReader();
            var lines = new List<LineImport>();
            while (reader.Read())
                lines.Add(new LineImport(reader.GetInt32(0), reader.GetInt32(1),
                    Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3))));
            return lines;
        }

        public List<int> GetUnfinishedImports()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT bl.""BatchId""
                  FROM ""BatchLines"" bl
                  WHERE bl.""Quantity"" > (SELECT COUNT(*) FROM ""Pallets"" p WHERE p.""BatchLineId"" = bl.""Id"")
                  GROUP BY bl.""BatchId""
                  ORDER BY bl.""BatchId""", conn);

            using var reader = cmd.ExecuteReader();
            var ids = new List<int>();
            while (reader.Read())
                ids.Add(reader.GetInt32(0));
            return ids;
        }

        public bool BatchExists(int batchId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT EXISTS (SELECT 1 FROM ""Batches"" WHERE ""Id"" = @id)", conn);
            cmd.Parameters.AddWithValue("id", batchId);
            return (bool)cmd.ExecuteScalar()!;
        }

        public int CountUnplacedPallets(int batchId, int afterId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT COUNT(*)
                  FROM ""Pallets"" p
                  JOIN ""BatchLines"" bl ON bl.""Id"" = p.""BatchLineId""
                  WHERE bl.""BatchId"" = @batchId
                    AND p.""Id"" > @afterId
                    AND NOT EXISTS (SELECT 1 FROM ""Slots"" s WHERE s.""PalletId"" = p.""Id"")", conn);
            cmd.Parameters.AddWithValue("batchId", batchId);
            cmd.Parameters.AddWithValue("afterId", afterId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<Pallet> GetUnplacedPallets(int batchId, int afterId, int limit)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT p.""Id"", p.""Code"", p.""BatchLineId"", bl.""ProductTypeId"",
                         t.""Id"", t.""Code"", t.""Length"", t.""Width"", t.""Height"", t.""Weight"", t.""Color""
                  FROM ""Pallets"" p
                  JOIN ""BatchLines""  bl ON bl.""Id"" = p.""BatchLineId""
                  JOIN ""PalletTypes"" t  ON t.""Id""  = bl.""PalletTypeId""
                  WHERE bl.""BatchId"" = @batchId
                    AND p.""Id"" > @afterId
                    AND NOT EXISTS (SELECT 1 FROM ""Slots"" s WHERE s.""PalletId"" = p.""Id"")
                  ORDER BY p.""Id""
                  LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("batchId", batchId);
            cmd.Parameters.AddWithValue("afterId", afterId);
            cmd.Parameters.AddWithValue("limit", limit);

            using var reader = cmd.ExecuteReader();
            var types = new Dictionary<int, PalletType>();
            var pallets = new List<Pallet>();
            while (reader.Read())
            {
                int typeId = reader.GetInt32(4);
                if (!types.TryGetValue(typeId, out var type))
                {
                    type = new PalletType
                    {
                        Id = typeId,
                        Code = reader.GetString(5),
                        Length = reader.GetDouble(6),
                        Width = reader.GetDouble(7),
                        Height = reader.GetDouble(8),
                        Weight = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                        Color = reader.GetString(10),
                    };
                    types[typeId] = type;
                }

                pallets.Add(new Pallet
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    BatchLineId = reader.GetInt32(2),
                    ProductTypeId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Type = type,
                });
            }
            return pallets;
        }

        public int PlacePallets(int[] slotIds, int[] palletIds, AllocationProgress progress)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using var cmd = new NpgsqlCommand(
                @"UPDATE ""Slots"" s
                  SET ""PalletId"" = t.pallet_id
                  FROM unnest(@slotIds, @palletIds) AS t(slot_id, pallet_id)
                  WHERE s.""Id"" = t.slot_id
                    AND s.""PalletId"" IS NULL
                    AND s.""Active"" = true", conn, tx);
            cmd.Parameters.AddWithValue("slotIds", slotIds);
            cmd.Parameters.AddWithValue("palletIds", palletIds);
            cmd.CommandTimeout = 300;

            int updated = cmd.ExecuteNonQuery();
            if (updated != slotIds.Length)
            {
                tx.Rollback();
                return -1;
            }

            UpsertAllocation(conn, tx, progress);
            tx.Commit();
            return updated;
        }

        public AllocationProgress? GetAllocation(int batchId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(AllocationSelect + @" WHERE ""BatchId"" = @batchId", conn);
            cmd.Parameters.AddWithValue("batchId", batchId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return ReadAllocation(reader);
        }

        public List<AllocationProgress> GetUnfinishedAllocations()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                AllocationSelect + @" WHERE ""Status"" IN ('Queued', 'Running') ORDER BY ""UpdatedAt""", conn);

            using var reader = cmd.ExecuteReader();
            var list = new List<AllocationProgress>();
            while (reader.Read())
                list.Add(ReadAllocation(reader));
            return list;
        }

        public void SaveAllocation(AllocationProgress progress)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            UpsertAllocation(conn, null, progress);
        }

        private const string AllocationSelect = @"
            SELECT ""BatchId"", ""RoomId"", ""Status"", ""AfterId"", ""Total"", ""Placed"", ""RejectedCount"", ""Error""
            FROM ""AllocationJobs""";

        private static AllocationProgress ReadAllocation(NpgsqlDataReader reader)
        {
            return new AllocationProgress(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7));
        }

        private static void UpsertAllocation(NpgsqlConnection conn, NpgsqlTransaction? tx, AllocationProgress progress)
        {
            using var cmd = new NpgsqlCommand(
                @"INSERT INTO ""AllocationJobs""
                      (""BatchId"", ""RoomId"", ""Status"", ""AfterId"", ""Total"", ""Placed"", ""RejectedCount"", ""Error"", ""UpdatedAt"")
                  VALUES (@batchId, @roomId, @status, @afterId, @total, @placed, @rejectedCount, @error, now())
                  ON CONFLICT (""BatchId"") DO UPDATE SET
                      ""RoomId"" = EXCLUDED.""RoomId"",
                      ""Status"" = EXCLUDED.""Status"",
                      ""AfterId"" = EXCLUDED.""AfterId"",
                      ""Total"" = EXCLUDED.""Total"",
                      ""Placed"" = EXCLUDED.""Placed"",
                      ""RejectedCount"" = EXCLUDED.""RejectedCount"",
                      ""Error"" = EXCLUDED.""Error"",
                      ""UpdatedAt"" = now()", conn, tx);
            cmd.Parameters.AddWithValue("batchId", progress.BatchId);
            cmd.Parameters.AddWithValue("roomId", progress.RoomId);
            cmd.Parameters.AddWithValue("status", progress.Status);
            cmd.Parameters.AddWithValue("afterId", progress.AfterId);
            cmd.Parameters.AddWithValue("total", progress.Total);
            cmd.Parameters.AddWithValue("placed", progress.Placed);
            cmd.Parameters.AddWithValue("rejectedCount", progress.RejectedCount);
            cmd.Parameters.Add(new NpgsqlParameter("error", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)progress.Error ?? DBNull.Value });
            cmd.ExecuteNonQuery();
        }
    }
}
