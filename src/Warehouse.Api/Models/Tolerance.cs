namespace Warehouse.Api.Models;
public static class Tolerance
{
    public const double Epsilon = 1e-9;
    public static bool AtMost(double a, double b) => a <= b + Epsilon;
}
