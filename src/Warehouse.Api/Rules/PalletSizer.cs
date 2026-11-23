namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

public record SizeResolution(PalletType Type, bool IsNew);

public static class PalletSizer
{
    private const double Epsilon = 1e-9;

    private static readonly string[] Palette =
        ["#e4572e", "#2e86ab", "#f2a541", "#59a14f", "#9c6ade", "#e15759", "#4e79a7", "#76b7b2", "#edc948", "#b07aa1"];

    public static SizeResolution Resolve(IReadOnlyList<PalletType> existing,
                                         double length, double width, double height, double? weight)
    {
        double longSide = Math.Max(length, width);
        double shortSide = Math.Min(length, width);

        foreach (var type in existing)
        {
            if (Same(Math.Max(type.Length, type.Width), longSide)
                && Same(Math.Min(type.Length, type.Width), shortSide)
                && Same(type.Height, height)
                && SameWeight(type.Weight, weight))
                return new SizeResolution(type, false);
        }

        var usedCodes = existing.Select(t => t.Code).ToHashSet();
        int index = existing.Count;
        while (usedCodes.Contains(CodeOf(index))) index++;

        var created = new PalletType
        {
            Code = CodeOf(index),
            Length = length,
            Width = width,
            Height = height,
            Weight = weight,
            Color = Palette[index % Palette.Length],
        };
        return new SizeResolution(created, true);
    }

    public static string CodeOf(int index)
    {
        string code = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26)
            code = (char)('A' + (n - 1) % 26) + code;
        return code;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) <= Epsilon;

    private static bool SameWeight(double? a, double? b)
        => a == null || b == null ? a == null && b == null : Same(a.Value, b.Value);
}
