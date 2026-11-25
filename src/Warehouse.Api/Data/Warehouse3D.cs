using Npgsql;
using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Data
{
    public class Warehouse3D(IConfiguration config) : IWarehouse3D
    {
        private readonly string _connectionString = config.GetConnectionString("Warehouse")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Warehouse.");

        public RoomInfoViewModel? GetRoom(int roomId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT ""Id"", ""Name"", ""Width"", ""Depth"", ""Height""
                  FROM ""Rooms""
                  WHERE ""Id"" = @id", conn);
            cmd.Parameters.AddWithValue("id", roomId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new RoomInfoViewModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Width = reader.GetDouble(2),
                Depth = reader.GetDouble(3),
                Height = reader.GetDouble(4),
            };
        }

        public (int RackCount, int SlotCount) GetRoomCounts(int roomId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT (SELECT COUNT(*) FROM ""Racks"" WHERE ""RoomId"" = @id),
                         (SELECT COALESCE(SUM(k.""Bays""), 0)
                          FROM ""Levels"" l
                          JOIN ""Racks"" k ON k.""Id"" = l.""RackId""
                          WHERE k.""RoomId"" = @id)", conn);
            cmd.Parameters.AddWithValue("id", roomId);

            using var reader = cmd.ExecuteReader();
            reader.Read();
            return (Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)));
        }

        public List<RackInfoViewModel> GetRacks(int roomId, ViewRect? view)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT k.""Id"", k.""Code"", k.""X"", k.""Y"", k.""Rotation"",
                         k.""Bays"", k.""BayWidth"", k.""Depth"", k.""ProductTypeId"",
                         l.""Id"", l.""Number"", l.""Height"", l.""MaxWeight"", l.""IsTop""
                  FROM ""Racks"" k
                  LEFT JOIN ""Levels"" l ON l.""RackId"" = k.""Id""
                  WHERE k.""RoomId"" = @id" + (view == null ? "" : ViewCondition) + @"
                  ORDER BY k.""Id"", l.""Number""", conn);
            cmd.Parameters.AddWithValue("id", roomId);
            AddViewParameters(cmd, view);

            using var reader = cmd.ExecuteReader();

            var racks = new List<RackInfoViewModel>();
            var rackById = new Dictionary<int, RackInfoViewModel>();

            while (reader.Read())
            {
                int rackId = reader.GetInt32(0);
                if (!rackById.TryGetValue(rackId, out var rack))
                {
                    rack = new RackInfoViewModel
                    {
                        Id = rackId,
                        Code = reader.GetString(1),
                        X = reader.GetDouble(2),
                        Y = reader.GetDouble(3),
                        Rotation = reader.GetInt32(4),
                        Bays = reader.GetInt32(5),
                        BayWidth = reader.GetDouble(6),
                        Depth = reader.GetDouble(7),
                        ProductTypeId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    };
                    rackById[rackId] = rack;
                    racks.Add(rack);
                }

                if (reader.IsDBNull(9)) continue;
                rack.Levels.Add(new LevelInfoViewModel
                {
                    Id = reader.GetInt32(9),
                    Number = reader.GetInt32(10),
                    Height = reader.GetDouble(11),
                    MaxWeight = reader.GetDouble(12),
                    IsTop = reader.GetBoolean(13),
                });
            }

            return racks;
        }

        private const string ViewCondition = @"
                    AND k.""X"" < @x2
                    AND k.""X"" + CASE WHEN k.""Rotation"" = 90 THEN k.""Depth"" ELSE k.""Bays"" * k.""BayWidth"" END > @x1
                    AND k.""Y"" < @y2
                    AND k.""Y"" + CASE WHEN k.""Rotation"" = 90 THEN k.""Bays"" * k.""BayWidth"" ELSE k.""Depth"" END > @y1";

        private static void AddViewParameters(NpgsqlCommand cmd, ViewRect? view)
        {
            if (view == null) return;
            cmd.Parameters.AddWithValue("x1", view.X1);
            cmd.Parameters.AddWithValue("y1", view.Y1);
            cmd.Parameters.AddWithValue("x2", view.X2);
            cmd.Parameters.AddWithValue("y2", view.Y2);
        }

        public List<SlotViewModel> GetSlots(int roomId, ViewRect? view)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT s.""Id"", l.""RackId"", s.""LevelId"", s.""Code"", l.""Number"", s.""Position"",
                         l.""IsTop"", s.""Active"", s.""PalletId"",
                         p.""Code"", t.""Code"", t.""Color"",
                         CASE WHEN t.""Length"" <= k.""BayWidth"" + 1e-9 AND t.""Width"" <= k.""Depth"" + 1e-9
                              THEN t.""Length"" ELSE t.""Width"" END,
                         CASE WHEN t.""Length"" <= k.""BayWidth"" + 1e-9 AND t.""Width"" <= k.""Depth"" + 1e-9
                              THEN t.""Width"" ELSE t.""Length"" END,
                         t.""Height"", bl.""ProductTypeId"", pt.""Name""
                  FROM ""Slots"" s
                  JOIN ""Levels"" l ON l.""Id"" = s.""LevelId""
                  JOIN ""Racks""  k ON k.""Id"" = l.""RackId""
                  LEFT JOIN ""Pallets""      p  ON p.""Id""  = s.""PalletId""
                  LEFT JOIN ""BatchLines""   bl ON bl.""Id"" = p.""BatchLineId""
                  LEFT JOIN ""PalletTypes""  t  ON t.""Id""  = bl.""PalletTypeId""
                  LEFT JOIN ""ProductTypes"" pt ON pt.""Id"" = bl.""ProductTypeId""
                  WHERE k.""RoomId"" = @id" + (view == null ? "" : ViewCondition) + @"
                  ORDER BY k.""Id"", l.""Number"", s.""Position""", conn);
            cmd.Parameters.AddWithValue("id", roomId);
            AddViewParameters(cmd, view);

            using var reader = cmd.ExecuteReader();
            var slots = new List<SlotViewModel>();
            while (reader.Read())
            {
                slots.Add(new SlotViewModel
                {
                    Id = reader.GetInt32(0),
                    RackId = reader.GetInt32(1),
                    LevelId = reader.GetInt32(2),
                    Code = reader.GetString(3),
                    Level = reader.GetInt32(4),
                    Bay = reader.GetInt32(5),
                    IsTop = reader.GetBoolean(6),
                    Active = reader.GetBoolean(7),
                    PalletId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    PalletCode = reader.IsDBNull(9) ? null : reader.GetString(9),
                    TypeCode = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Color = reader.IsDBNull(11) ? null : reader.GetString(11),
                    PalletAlongBay = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                    PalletAcrossDepth = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                    PalletHeight = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                    ProductTypeId = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                    ProductTypeName = reader.IsDBNull(16) ? null : reader.GetString(16),
                });
            }
            return slots;
        }
    }
}
