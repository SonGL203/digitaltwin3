namespace Warehouse.Api.Models;

/// <summary>Số ô lưới mà một kệ chiếm trên mặt bằng: Cols ô theo trục X, Rows ô theo trục Y.</summary>
public readonly record struct Footprint(int Cols, int Rows);
