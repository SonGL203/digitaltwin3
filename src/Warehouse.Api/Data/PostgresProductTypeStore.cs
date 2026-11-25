namespace Warehouse.Api.Data;

using Npgsql;
using Warehouse.Api.ViewModels;

public class PostgresProductTypeStore(IConfiguration config) : IProductTypeStore
{
    private readonly string _connectionString = config.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Warehouse.");

    public IReadOnlyList<ProductTypeViewModel> List()
    {
        var types = new List<ProductTypeViewModel>();
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"SELECT ""Id"", ""Code"", ""Name"" FROM ""ProductTypes"" ORDER BY ""Code""", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            types.Add(new ProductTypeViewModel(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return types;
    }

    public bool CodeExists(string code)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"SELECT EXISTS (SELECT 1 FROM ""ProductTypes"" WHERE lower(""Code"") = lower(@code))", conn);
        cmd.Parameters.AddWithValue("code", code);
        return (bool)cmd.ExecuteScalar()!;
    }

    public ProductTypeViewModel Create(string code, string name)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand(
            @"INSERT INTO ""ProductTypes"" (""Code"", ""Name"") VALUES (@code, @name) RETURNING ""Id""", conn);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", name);
        int id = (int)cmd.ExecuteScalar()!;
        return new ProductTypeViewModel(id, code, name);
    }
}
