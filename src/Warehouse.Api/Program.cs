var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Nối dây interface → lớp thật đặt ở đây, ví dụ:
// builder.Services.AddSingleton<IWarehouseStore, InMemoryWarehouseStore>();
// builder.Services.AddScoped<IPalletService, PalletService>();

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
