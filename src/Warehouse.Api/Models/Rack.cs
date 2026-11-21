namespace Warehouse.Api.Models;

// Một kệ. Mỗi kệ tự mang cấu hình của nó (số tầng, số ô, kích thước...). Đơn vị: mét, kg.
public class Rack
{
    public int Id { get; set; }
    public string Code { get; set; } = "";     // K1, K2...
    public double X { get; set; }              // góc trên trái của kệ trên mặt bằng
    public double Y { get; set; }
    public int Rotation { get; set; }          // 0 hoặc 90 độ
    public int Levels { get; set; }            // số tầng thường (KHÔNG tính nóc)
    public double LevelHeight { get; set; }    // chiều cao mỗi tầng
    public int Bays { get; set; }              // số ô trong một tầng
    public double BayWidth { get; set; }       // chiều rộng một ô
    public double Depth { get; set; }          // chiều sâu kệ
    public bool HasTop { get; set; }           // có thêm hàng khung "nóc" trên cùng không
    public double MaxWeight { get; set; }
    public int? ProductTypeId { get; set; }    // null = kệ chung

    // Luật của chính cái kệ: nó chiếm bao nhiêu chỗ trên sàn.
    // Không xoay: dài = số ô × rộng ô, sâu = Depth. Xoay 90° thì đổi chỗ hai cạnh.
    public Rect GetBounds()
    {
        double length = Bays * BayWidth;
        return Rotation == 90
            ? new Rect(X, Y, Depth, length)
            : new Rect(X, Y, length, Depth);
    }
}
