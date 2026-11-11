using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Data;
using Warehouse.Api.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException(
            "Thiếu chuỗi kết nối ConnectionStrings:Warehouse (đặt bằng dotnet user-secrets).")));
builder.Services.AddScoped<IWarehouseStore, EfWarehouseStore>();
builder.Services.AddScoped<ILayoutService, LayoutService>();
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "WarehouseTwin API"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
