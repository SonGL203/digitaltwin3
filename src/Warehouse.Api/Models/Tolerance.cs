namespace Warehouse.Api.Models;
public static class Tolerance
{
    public const double Epsilon = 1e-9;
    public static bool AtMost(double a, double b) => a <= b + Epsilon;
    public static bool AreEqual(double a, double b) => Math.Abs(a - b) <= Epsilon;
    /// <summary>a nhỏ hơn b một cách chắc chắn (không tính sai số làm tròn).</summary>
    public static bool LessThan(double a, double b) => a < b - Epsilon;
}
