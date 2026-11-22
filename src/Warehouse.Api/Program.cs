using Warehouse.Api.Data;
using Warehouse.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddScoped<IWarehouseStore, PostgresWarehouseStore>();
builder.Services.AddScoped<ILayoutStore, PostgresLayoutStore>();
builder.Services.AddScoped<IRoomService, RoomService>();
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
