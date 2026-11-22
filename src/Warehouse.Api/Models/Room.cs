<<<<<<< HEAD
﻿using System;
using System.Collections.Generic;

namespace Warehouse.Api.Models;

public partial class Room
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public double Width { get; set; }

    public double Depth { get; set; }

    public double Height { get; set; }

    public double GridSize { get; set; }

    public double MinGap { get; set; }

    public double Margin { get; set; }

    public int Version { get; set; }

    public virtual ICollection<Obstacle> Obstacles { get; set; } = new List<Obstacle>();

    public virtual ICollection<Rack> Racks { get; set; } = new List<Rack>();
=======
namespace Warehouse.Api.Models;

// Một phòng (kho). Sàn là hình chữ nhật Width × Depth mét, góc trên trái ở (0, 0).
public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double GridSize { get; set; }       // bước "hít" lưới khi vẽ, chỉ dùng cho giao diện
    public double MinGap { get; set; }         // lối đi tối thiểu giữa hai kệ
    public double Margin { get; set; }         // khoảng hở trên đỉnh pallet
    public int Version { get; set; }           // tăng mỗi lần lưu, để phát hiện hai người sửa cùng lúc
    public List<Rack> Racks { get; set; } = [];
    public List<Obstacle> Obstacles { get; set; } = [];
>>>>>>> main
}
