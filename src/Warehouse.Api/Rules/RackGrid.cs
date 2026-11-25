namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

/// <summary>
/// Lưới chia sàn thành các ô vuông, mỗi ô nhớ những kệ phủ lên nó. Dùng để tìm nhanh các kệ ở gần một kệ
/// (đè lên hoặc cách dưới MinGap) mà không phải so với mọi kệ trong phòng.
/// Chỉ để lọc bớt: luật đè / lối đi vẫn do PlacementValidator tính chính xác trên các kệ được trả ra.
/// </summary>
internal sealed class RackGrid
{
    private readonly IReadOnlyList<Rack> _racks;
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly double _cell;
    private readonly double _minX, _minY, _maxX, _maxY;

    public RackGrid(Room room)
    {
        _racks = room.Racks;

        // Vùng cần quan tâm: sàn phòng nới ra một đoạn lối đi (kệ hợp lệ nằm trong sàn, chỉ "với" ra tối đa MinGap)
        double pad = room.MinGap + 1;
        _minX = -pad; _minY = -pad;
        _maxX = room.Width + pad; _maxY = room.Depth + pad;

        // Ô lưới cỡ một kệ trung bình: mỗi kệ phủ vài ô, mỗi ô chứa vài kệ
        double sum = 0;
        int n = 0;
        foreach (var r in _racks)
        {
            var b = r.GetBounds();
            if (!IsFinite(b)) continue;
            sum += Math.Max(Math.Abs(b.Width), Math.Abs(b.Depth));
            n++;
        }
        double avg = n == 0 ? 1 : sum / n;
        _cell = Math.Clamp(Math.Max(avg, room.MinGap), 0.5, 50);

        for (int i = 0; i < _racks.Count; i++)
        {
            var b = _racks[i].GetBounds();
            if (!IsFinite(b)) continue;   // toạ độ NaN/∞: so sánh nào cũng ra false, bỏ qua giống hệt khi không có lưới
            int index = i;
            ForEachCell(b, 0, key =>
            {
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                list.Add(index);
            });
        }
    }

    /// <summary>Các kệ có thể đè hoặc cách box dưới gap, theo đúng thứ tự trong room.Racks (có thể dư, không bao giờ thiếu).</summary>
    public IEnumerable<Rack> Near(Rect box, double gap)
    {
        if (!IsFinite(box)) return _racks;
        var found = new SortedSet<int>();
        ForEachCell(box, gap + PlacementValidator.Eps, key =>
        {
            if (_cells.TryGetValue(key, out var list)) found.UnionWith(list);
        });
        return found.Select(i => _racks[i]);
    }

    private void ForEachCell(Rect b, double grow, Action<long> visit)
    {
        double x1 = Math.Min(b.X, b.X + b.Width) - grow, x2 = Math.Max(b.X, b.X + b.Width) + grow;
        double y1 = Math.Min(b.Y, b.Y + b.Depth) - grow, y2 = Math.Max(b.Y, b.Y + b.Depth) + grow;
        int cx1 = Index(x1, _minX, _maxX), cx2 = Index(x2, _minX, _maxX);
        int cy1 = Index(y1, _minY, _maxY), cy2 = Index(y2, _minY, _maxY);
        for (int cx = cx1; cx <= cx2; cx++)
            for (int cy = cy1; cy <= cy2; cy++)
                visit(((long)cx << 32) | (uint)cy);
    }

    /// <summary>Số thứ tự ô theo một trục; ngoài vùng quan tâm thì dồn vào ô biên (dư thì không sao).</summary>
    private int Index(double v, double min, double max) => (int)Math.Floor((Math.Clamp(v, min, max) - min) / _cell);

    private static bool IsFinite(Rect b) =>
        double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Width) && double.IsFinite(b.Depth);
}
