using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> GetProducts()
    {
        var list = await _db.Products
            .AsNoTracking()
            .Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                Stock = p.Stock
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                Stock = p.Stock
            })
            .SingleOrDefaultAsync();

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] ProductCreateDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var product = new Product
        {
            Name = dto.Name.Trim(),
            Price = dto.Price,
            Stock = dto.Stock
        };

        await _db.Products.AddAsync(product);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductCreateDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.Name = dto.Name.Trim();
        product.Price = dto.Price;
        product.Stock = dto.Stock;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(product));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        if (await _db.OrderItems.AnyAsync(item => item.ProductId == id))
            return Conflict("Products referenced by existing orders cannot be deleted.");

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static ProductResponseDto ToResponse(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Price = product.Price,
        Stock = product.Stock
    };
}
