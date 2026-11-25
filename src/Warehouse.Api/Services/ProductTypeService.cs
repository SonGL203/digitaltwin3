namespace Warehouse.Api.Services;

using Npgsql;
using Warehouse.Api.Data;
using Warehouse.Api.ViewModels;

public interface IProductTypeService
{
    IReadOnlyList<ProductTypeViewModel> List();

    /// <summary>Thêm loại hàng. 400 sai số liệu, 409 trùng mã.</summary>
    ServiceResult<ProductTypeViewModel> Create(CreateProductTypeRequest request);
}

public class ProductTypeService(IProductTypeStore store) : IProductTypeService
{
    public IReadOnlyList<ProductTypeViewModel> List() => store.List();

    public ServiceResult<ProductTypeViewModel> Create(CreateProductTypeRequest request)
    {
        string code = (request.Code ?? "").Trim();
        string name = (request.Name ?? "").Trim();
        if (code.Length == 0) return ServiceResult<ProductTypeViewModel>.Fail(400, "Chưa nhập mã loại hàng.");
        if (code.Length > 20) return ServiceResult<ProductTypeViewModel>.Fail(400, "Mã loại hàng tối đa 20 ký tự.");
        if (name.Length == 0) return ServiceResult<ProductTypeViewModel>.Fail(400, "Chưa nhập tên loại hàng.");
        if (name.Length > 50) return ServiceResult<ProductTypeViewModel>.Fail(400, "Tên loại hàng tối đa 50 ký tự.");

        string duplicate = $"Đã có loại hàng mã {code}.";
        if (store.CodeExists(code)) return ServiceResult<ProductTypeViewModel>.Fail(409, duplicate);

        try
        {
            return ServiceResult<ProductTypeViewModel>.Ok(store.Create(code, name));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return ServiceResult<ProductTypeViewModel>.Fail(409, duplicate);   // hai người thêm cùng mã cùng lúc
        }
    }
}
