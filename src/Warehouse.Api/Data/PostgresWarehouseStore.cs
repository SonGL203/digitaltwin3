namespace Warehouse.Api.Data;

using Npgsql;
using Warehouse.Api.Models;

public class PostgresWarehouseStore(IConfiguration config) : IWarehouseStore
{
    private readonly string _connectionString = config.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Warehouse.");

    public Room? GetRoom(int id)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        Room? room = null;
        using (var cmd = new NpgsqlCommand(
            @"SELECT ""Id"", ""Name"", ""Width"", ""Depth"", ""Height"", ""GridSize"", ""MinGap"", ""Margin"", ""Version""
              FROM ""Rooms"" WHERE ""Id"" = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                room = new Room
                {
                    Id = reader.GetInt32(0), Name = reader.GetString(1),
                    Width = reader.GetDouble(2), Depth = reader.GetDouble(3), Height = reader.GetDouble(4),
                    GridSize = reader.GetDouble(5), MinGap = reader.GetDouble(6), Margin = reader.GetDouble(7),
                    Version = reader.GetInt32(8),
                };
            }
        }
        if (room == null) return null;

        using (var cmd = new NpgsqlCommand(
            @"SELECT k.""Id"", k.""Code"", k.""X"", k.""Y"", k.""Rotation"",
                     COUNT(l.""Id"") FILTER (WHERE NOT l.""IsTop""),
                     COALESCE(MAX(l.""Height"") FILTER (WHERE NOT l.""IsTop""), 0),
                     k.""Bays"", k.""BayWidth"", k.""Depth"",
                     COALESCE(BOOL_OR(l.""IsTop""), false),
                     COALESCE(MAX(l.""MaxWeight"") FILTER (WHERE NOT l.""IsTop""), 0),
                     k.""ProductTypeId""
              FROM ""Racks"" k
              LEFT JOIN ""Levels"" l ON l.""RackId"" = k.""Id""
              WHERE k.""RoomId"" = @id
              GROUP BY k.""Id""
              ORDER BY k.""Id""", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                room.Racks.Add(new Rack
                {
                    Id = reader.GetInt32(0), Code = reader.GetString(1),
                    X = reader.GetDouble(2), Y = reader.GetDouble(3), Rotation = reader.GetInt32(4),
                    Levels = (int)reader.GetInt64(5),
                    LevelHeight = reader.GetDouble(6),
                    Bays = reader.GetInt32(7), BayWidth = reader.GetDouble(8), Depth = reader.GetDouble(9),
                    HasTop = reader.GetBoolean(10), MaxWeight = reader.GetDouble(11),
                    ProductTypeId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                });
            }
        }

        using (var cmd = new NpgsqlCommand(
            @"SELECT ""Id"", ""Kind"", ""X"", ""Y"", ""Width"", ""Depth""
              FROM ""Obstacles"" WHERE ""RoomId"" = @id ORDER BY ""Id""", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                room.Obstacles.Add(new Obstacle
                {
                    Id = reader.GetInt32(0), Kind = Enum.Parse<ObstacleKind>(reader.GetString(1)),
                    X = reader.GetDouble(2), Y = reader.GetDouble(3),
                    Width = reader.GetDouble(4), Depth = reader.GetDouble(5),
                });
            }
        }

        return room;
    }

    public IReadOnlyList<RoomSummary> GetRoomSummaries()
    {
        var rooms = new List<RoomSummary>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT r.""Id"", r.""Name"", r.""Width"", r.""Depth"", r.""Height"", r.""GridSize"",
                     (SELECT COUNT(*) FROM ""Racks"" k WHERE k.""RoomId"" = r.""Id""),
                     (SELECT COUNT(*) FROM ""Slots"" s
                        JOIN ""Levels"" l ON l.""Id"" = s.""LevelId""
                        JOIN ""Racks"" k ON k.""Id"" = l.""RackId""
                      WHERE k.""RoomId"" = r.""Id""),
                     (SELECT COUNT(*) FROM ""Obstacles"" o WHERE o.""RoomId"" = r.""Id"")
              FROM ""Rooms"" r
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

    public bool RoomNameExists(string name)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT EXISTS (SELECT 1 FROM ""Rooms"" WHERE lower(""Name"") = lower(@name))", conn);
        cmd.Parameters.AddWithValue("name", name);
        return (bool)cmd.ExecuteScalar()!;
    }

    public int AddRoom(Room room)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"INSERT INTO ""Rooms"" (""Name"", ""Width"", ""Depth"", ""Height"", ""GridSize"", ""MinGap"", ""Margin"")
              VALUES (@name, @width, @depth, @height, @gridSize, @minGap, @margin)
              RETURNING ""Id""", conn);
        cmd.Parameters.AddWithValue("name", room.Name);
        cmd.Parameters.AddWithValue("width", room.Width);
        cmd.Parameters.AddWithValue("depth", room.Depth);
        cmd.Parameters.AddWithValue("height", room.Height);
        cmd.Parameters.AddWithValue("gridSize", room.GridSize);
        cmd.Parameters.AddWithValue("minGap", room.MinGap);
        cmd.Parameters.AddWithValue("margin", room.Margin);
        return (int)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Slot> GetSlotsOfRacks(IReadOnlyCollection<int> rackIds)
    {
        var slots = new List<Slot>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT s.""Id"", l.""RackId"", s.""Code"", l.""Number"", s.""Position"", l.""IsTop""
              FROM ""Slots"" s
              JOIN ""Levels"" l ON l.""Id"" = s.""LevelId""
              WHERE l.""RackId"" = ANY(@ids)
              ORDER BY l.""RackId"", l.""Number"", s.""Position""", conn);
        cmd.Parameters.AddWithValue("ids", rackIds.ToArray());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            slots.Add(new Slot
            {
                Id = reader.GetInt32(0), RackId = reader.GetInt32(1), Code = reader.GetString(2),
                Level = reader.GetInt32(3), Bay = reader.GetInt32(4), IsTop = reader.GetBoolean(5),
            });
        }
        return slots;
    }
}
