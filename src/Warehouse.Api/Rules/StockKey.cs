namespace Warehouse.Api.Rules;

using Warehouse.Api.Models;

// Hàng trong một khung: loại hàng + cỡ pallet.
public readonly record struct StockKey(int? ProductTypeId, string SizeCode)
{
    // Nhóm dùng để gom theo kệ: cùng loại hàng thì chung kệ.
    // Pallet chưa phân loại: mỗi cỡ một nhóm (giữ cách xếp cũ).
    public string RackGroup => ProductTypeId is int id ? $"P{id}" : $"S:{SizeCode}";

    public static StockKey Of(Pallet pallet) => new(pallet.ProductTypeId, pallet.Size.Code);
}
