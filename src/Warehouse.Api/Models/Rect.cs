namespace Warehouse.Api.Models;

// Một hình chữ nhật trên mặt bằng, đơn vị mét.
// (X, Y) là góc trên trái, Width chạy theo trục X, Depth chạy theo trục Y.
public record struct Rect(double X, double Y, double Width, double Depth);
