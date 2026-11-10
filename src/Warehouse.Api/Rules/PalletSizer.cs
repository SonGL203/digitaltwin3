namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

/// <summary>Kết quả tìm type: Size là type dùng được; IsNew = true nghĩa là type vừa tạo, service phải lưu.</summary>
public sealed record SizeResolution(PalletSize Size, bool IsNew);

/// <summary>
/// Tìm type (PalletSize) cho một dòng nhập "dài × rộng × cao (+ khối lượng)": có type đúng số đo đó thì dùng lại,
/// chưa có thì tạo type mới (mã A, B, … Z, AA, AB…; màu lấy lần lượt trong bảng màu).
/// Hàm thuần: không đọc store, không tự thêm type mới vào danh sách; việc lưu là của service.
/// </summary>
public static class PalletSizer
{
    // Màu chỉ để phân biệt khi tô 3D, không có ý nghĩa nghiệp vụ; hết bảng thì quay lại từ đầu.
    private static readonly string[] Palette =
    {
        "#1D9E75", "#7F77DD", "#D85A30", "#D4537E", "#378ADD",
        "#639922", "#BA7517", "#E24B4A", "#534AB7", "#0F6E56",
    };

    /// <summary>
    /// Cùng type khi: cạnh dài, cạnh ngắn (không phân biệt dài hay rộng, vì xoay 90° vẫn là một pallet), chiều cao
    /// và khối lượng (cả hai cùng null cũng tính là giống) đều bằng nhau, cho phép sai số nhỏ.
    /// Type mới lưu Length là cạnh dài hơn, Width là cạnh ngắn hơn.
    /// </summary>
    public static SizeResolution Resolve(
        IReadOnlyList<PalletSize> existing, double length, double width, double height, double? weight)
    {
        ArgumentNullException.ThrowIfNull(existing);

        // Kiểm tra trước khi chuẩn hoá để báo đúng tên tham số người dùng nhập (length, width, height).
        if (length <= 0)
            throw new ArgumentException("Chiều dài phải lớn hơn 0.", nameof(length));
        if (width <= 0)
            throw new ArgumentException("Chiều rộng phải lớn hơn 0.", nameof(width));
        if (height <= 0)
            throw new ArgumentException("Chiều cao phải lớn hơn 0.", nameof(height));
        if (weight is <= 0)
            throw new ArgumentException("Khối lượng phải lớn hơn 0 (hoặc để trống).", nameof(weight));

        double longSide = Math.Max(length, width);
        double shortSide = Math.Min(length, width);

        foreach (var size in existing)
        {
            if (Matches(size, longSide, shortSide, height, weight))
                return new SizeResolution(size, IsNew: false);
        }

        var created = new PalletSize(
            NextCode(existing), longSide, shortSide, height, Palette[existing.Count % Palette.Length], weight);

        return new SizeResolution(created, IsNew: true);
    }

    private static bool Matches(PalletSize size, double longSide, double shortSide, double height, double? weight)
    {
        return Tolerance.AreEqual(Math.Max(size.Length, size.Width), longSide)
            && Tolerance.AreEqual(Math.Min(size.Length, size.Width), shortSide)
            && Tolerance.AreEqual(size.Height, height)
            && SameWeight(size.Weight, weight);
    }

    private static bool SameWeight(double? a, double? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        return Tolerance.AreEqual(a.Value, b.Value);
    }

    /// <summary>Mã kế tiếp A, B, … Z, AA, AB…, bỏ qua mã đã có để không bao giờ trùng.</summary>
    private static string NextCode(IReadOnlyList<PalletSize> existing)
    {
        var used = existing.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = existing.Count; ; index++)
        {
            string code = CodeAt(index);
            if (!used.Contains(code))
                return code;
        }
    }

    /// <summary>Đánh số kiểu cột Excel, đếm từ 0: 0 → A, 25 → Z, 26 → AA, 27 → AB.</summary>
    private static string CodeAt(int index)
    {
        var letters = new Stack<char>();
        int n = index + 1;

        while (n > 0)
        {
            n--;
            letters.Push((char)('A' + n % 26));
            n /= 26;
        }

        return new string(letters.ToArray());
    }
}
