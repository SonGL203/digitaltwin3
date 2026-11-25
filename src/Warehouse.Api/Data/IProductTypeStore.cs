namespace Warehouse.Api.Data;

using Warehouse.Api.ViewModels;

/// <summary>Đọc / thêm loại hàng (bảng ProductTypes). Tách riêng để không đụng store chung.</summary>
public interface IProductTypeStore
{
    IReadOnlyList<ProductTypeViewModel> List();

    /// <summary>Đã có loại hàng cùng mã (không phân biệt hoa thường).</summary>
    bool CodeExists(string code);

    ProductTypeViewModel Create(string code, string name);
}
