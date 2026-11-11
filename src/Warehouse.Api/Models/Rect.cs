namespace Warehouse.Api.Models;

/// <summary>
/// Hình chữ nhật trên mặt bằng, đơn vị mét. (X, Y) là góc trên trái; Width theo trục X, Depth theo trục Y.
/// Mọi phép so sánh đi qua Tolerance: kệ đặt sát nhau 5,8 và 5,8000000001 phải coi là chạm, không phải đè.
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Depth)
{
    public double Right => X + Width;
    public double Bottom => Y + Depth;

    /// <summary>
    /// Hai hình có phần chung diện tích dương không. Chỉ chạm cạnh thì KHÔNG tính là đè
    /// (MinGap = 0 cho phép kệ liền nhau).
    /// </summary>
    public bool Overlaps(Rect other)
        => Tolerance.LessThan(X, other.Right) && Tolerance.LessThan(other.X, Right)
        && Tolerance.LessThan(Y, other.Bottom) && Tolerance.LessThan(other.Y, Bottom);

    /// <summary>Hình này nằm gọn trong outer (được phép sát mép).</summary>
    public bool IsInside(Rect outer)
        => Tolerance.AtMost(outer.X, X) && Tolerance.AtMost(Right, outer.Right)
        && Tolerance.AtMost(outer.Y, Y) && Tolerance.AtMost(Bottom, outer.Bottom);

    /// <summary>
    /// Khoảng trống giữa hai hình theo từng trục. Bằng 0 trên trục mà hình chiếu của hai hình chồng nhau.
    /// Ví dụ: K1 x 1,0→5,8; K2 x 7,0→11,8, cùng hàng → (Dx 1,2; Dy 0).
    /// </summary>
    public (double Dx, double Dy) GapTo(Rect other)
        => (Math.Max(0, Math.Max(other.X - Right, X - other.Right)),
            Math.Max(0, Math.Max(other.Y - Bottom, Y - other.Bottom)));
}
