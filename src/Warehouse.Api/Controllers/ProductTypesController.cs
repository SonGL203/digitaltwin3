namespace Warehouse.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Warehouse.Api.Services;
using Warehouse.Api.ViewModels;

[ApiController]
[Route("api/product-types")]
public class ProductTypesController(IProductTypeService productTypeService) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        return Ok(productTypeService.List());
    }

    [HttpPost]
    public IActionResult Create(CreateProductTypeRequest request)
    {
        var result = productTypeService.Create(request);
        if (result.Error != null)
            return Problem(detail: result.Error, statusCode: result.StatusCode);

        return Ok(result.Value);
    }
}
