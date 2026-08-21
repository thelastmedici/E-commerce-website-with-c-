using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetProducts()
    {
        var list = _db.Products.Select(p => new ProductResponseDto { Id = p.Id, Name = p.Name, Price = p.Price, Stock = p.Stock }).ToList();
        return Ok(list);
    }
    
    [HttpPost]
    public IActionResult Create([FromBody] ProductCreateDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var product = new Product
        {
            Name = dto.Name,
            Price = dto.Price
            ,Stock = dto.Stock
        };

        _db.Products.Add(product);
        _db.SaveChanges();

        var resp = new ProductResponseDto { Id = product.Id, Name = product.Name, Price = product.Price, Stock = product.Stock };
        return Ok(resp);
    }
}
