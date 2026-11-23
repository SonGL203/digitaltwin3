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

        public double? GetRoomMargin(int roomId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT ""Margin"" FROM ""Rooms"" WHERE ""Id"" = @id", conn);
            cmd.Parameters.AddWithValue("id", roomId);
            var value = cmd.ExecuteScalar();
            return value == null ? null : Convert.ToDouble(value);
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

            using var cmd = new NpgsqlCommand("SELECT COUNT(*)" + SlotJoins + FitCondition, conn);
            AddFitParameters(cmd, roomId, length, width, height, weight, productTypeId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<FreeSlot> GetFreeSlots(int roomId, PalletType type, int? productTypeId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT s.""Id"", s.""Code"", k.""Code"", l.""IsTop""" + SlotJoins + FitCondition +
                @" ORDER BY l.""IsTop"", k.""Id"", l.""Number"", s.""Position""", conn);
            AddFitParameters(cmd, roomId, type.Length, type.Width, type.Height, type.Weight, productTypeId);

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

                foreach (var pallet in line.Pallets)
                {
                    using var cmd = new NpgsqlCommand(
                        @"INSERT INTO ""Pallets"" (""Code"", ""BatchLineId"")
                          VALUES (@code, @batchLineId)
                          RETURNING ""Id""", conn, tx);
                    cmd.Parameters.AddWithValue("code", pallet.Code);
                    cmd.Parameters.AddWithValue("batchLineId", line.Id);
                    pallet.BatchLineId = line.Id;
                    pallet.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            tx.Commit();
        }

        public bool BatchExists(int batchId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(@"SELECT EXISTS (SELECT 1 FROM ""Batches"" WHERE ""Id"" = @id)", conn);
            cmd.Parameters.AddWithValue("id", batchId);
            return (bool)cmd.ExecuteScalar()!;
        }

        public List<Pallet> GetUnplacedPallets(int batchId)
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
                    AND NOT EXISTS (SELECT 1 FROM ""Slots"" s WHERE s.""PalletId"" = p.""Id"")
                  ORDER BY p.""Id""", conn);
            cmd.Parameters.AddWithValue("batchId", batchId);

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

        public int PlacePallets(int[] slotIds, int[] palletIds)
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

            tx.Commit();
            return updated;
        }
    }
}
