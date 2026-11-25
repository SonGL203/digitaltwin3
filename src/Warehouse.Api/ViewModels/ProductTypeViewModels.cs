namespace Warehouse.Api.ViewModels;

public record ProductTypeViewModel(int Id, string Code, string Name);

public class CreateProductTypeRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
