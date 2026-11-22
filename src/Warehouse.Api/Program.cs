using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Data;
using Warehouse.Api.Infrastructure;
using Warehouse.Api.Data;
using Warehouse.Api.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException(
            "Thiếu chuỗi kết nối ConnectionStrings:Warehouse (đặt bằng dotnet user-secrets).")));
// Service: controller chỉ biết interface, DI tự đưa bản cài đặt vào (mỗi request một bản, dùng chung DbContext của request đó).
builder.Services.AddScoped<IRoomService, RoomService>();
// Nối interface với lớp thật. Đổi nơi lưu dữ liệu chỉ cần sửa dòng IWarehouseStore (sau này: EfWarehouseStore).
builder.Services.AddScoped<IWarehouseStore, PostgresWarehouseStore>();
builder.Services.AddScoped<IRoomService, RoomService>();
var app = builder.Build();

app.UseExceptionHandler();

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
