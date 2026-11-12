using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Data;
using Warehouse.Api.Infrastructure;
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
builder.Services.AddScoped<IWarehouseStore, EfWarehouseStore>();
builder.Services.AddScoped<ILayoutService, LayoutService>();
builder.Services.AddScoped<IPalletService, PalletService>();
var app = builder.Build();

// Đầu pipeline để bắt mọi lỗi phía sau (xem ApiExceptionHandler).
app.UseExceptionHandler();

// Phòng mẫu cho demo: bật bằng "Seed:SampleLayout": true. Tắt khi muốn tự vẽ phòng từ đầu.
if (app.Configuration.GetValue<bool>("Seed:SampleLayout"))
{
    using (var scope = app.Services.CreateScope())
    {
        SampleLayoutSeeder.Seed(
            scope.ServiceProvider.GetRequiredService<ILayoutService>(),
            scope.ServiceProvider.GetRequiredService<ILogger<Program>>(),
            Path.Combine(app.Environment.ContentRootPath, "SampleData", "sample-layout.json"),
            scope.ServiceProvider.GetRequiredService<IPalletService>(),
            Path.Combine(app.Environment.ContentRootPath, "SampleData", "sample-batches.json"));
    }
}
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
