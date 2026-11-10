namespace Warehouse.Api.Models;

/// <summary>
/// Một pallet hàng thật. Mã do hệ thống sinh khi nhập lô (b1-P001…).
/// Số đo và khối lượng không lưu ở đây mà nằm ở Size (PalletSize): mọi pallet cùng type có thông số y hệt nhau,
/// nên chỉ lưu một lần ở type. Không lưu vị trí (Slot.PalletId giữ), không lưu hướng xoay.
/// ProductTypeId là loại hàng trên pallet (ốc vít, bánh xe…); null nghĩa là hàng chưa phân loại.
/// </summary>
public class Pallet
{
    public int Id { get; private set; }
    public int BatchId { get; private set; }
    public int SizeId { get; private set; }
    public PalletSize Size { get; private set; }
    public string Code { get; private set; }
    public int? ProductTypeId { get; private set; }

    public Pallet(int batchId, PalletSize size, string code, int? productTypeId = null)
    {
        ArgumentNullException.ThrowIfNull(size);
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Mã pallet không được rỗng.", nameof(code));

        BatchId = batchId;
        Size = size;
        SizeId = size.Id;
        Code = code.Trim();
        ProductTypeId = productTypeId;
    }
}
