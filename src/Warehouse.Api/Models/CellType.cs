namespace Warehouse.Api.Models;

/// <summary>Loại một ô lưới của căn phòng. Room lưu mỗi ô bằng một ký tự: '.' trống, '#' tường, 'O' vật cản.</summary>
public enum CellType
{
    Empty,
    Wall,
    Obstacle,
}
