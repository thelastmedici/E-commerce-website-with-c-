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
    public IActionResult GetProducts() => Ok(_db.Products.ToList());

    [HttpPost]
    public IActionResult Create(Product product)
    {
        _db.Products.Add(product);
        _db.SaveChanges();
        return Ok(product);
    }
}
