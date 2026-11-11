using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Data;
using Warehouse.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Nối dây interface → lớp thật (chỗ duy nhất).
// Chuỗi kết nối PostgreSQL đọc từ "ConnectionStrings:Warehouse": máy dev để trong dotnet user-secrets
// (ngoài repo, không lên git), máy khác đặt biến môi trường ConnectionStrings__Warehouse.
// Đọc bên trong lambda nên chỉ báo lỗi khi thật sự cần DB, test không đụng DB (ví dụ /api/health) vẫn chạy.
builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Warehouse")
        ?? throw new InvalidOperationException(
            "Thiếu chuỗi kết nối ConnectionStrings:Warehouse (đặt bằng dotnet user-secrets).")));

// Store Scoped theo DbContext (mỗi request một DbContext). InMemoryWarehouseStore vẫn giữ làm kho giả cho test.
builder.Services.AddScoped<IWarehouseStore, EfWarehouseStore>();
builder.Services.AddScoped<ILayoutService, LayoutService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Tài liệu API: /openapi/v1.json; giao diện thử API: /swagger
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "WarehouseTwin API"));
}

// Frontend đã build (npm run build trong frontend/) nằm trong wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Đường dẫn không phải /api thì trả index.html của frontend.
app.MapFallbackToFile("index.html");

app.Run();

// Cho phép WebApplicationFactory<Program> trong test truy cập.
public partial class Program { }
