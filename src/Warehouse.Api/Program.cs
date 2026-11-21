using Warehouse.Api.Data;
using Warehouse.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Nối interface với lớp thật. Đổi nơi lưu dữ liệu chỉ cần sửa dòng IWarehouseStore (sau này: EfWarehouseStore).
builder.Services.AddScoped<IWarehouseStore, SampleWarehouseStore>();
builder.Services.AddScoped<IRoomService, RoomService>();
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
