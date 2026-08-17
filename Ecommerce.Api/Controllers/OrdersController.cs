using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var orders = await _db.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .ToListAsync();

        var result = orders.Select(o => new OrderResponseDto(
            o.Id,
            o.UserId,
            o.CreatedAt,
            o.Total,
            o.Items.Select(i => new OrderItemResponseDto(
                i.ProductId,
                products.TryGetValue(i.ProductId, out var prod) ? prod.Name : string.Empty,
                i.Quantity,
                i.Price
            )).ToList()
        ));

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var products = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);

        var dto = new OrderResponseDto(
            order.Id,
            order.UserId,
            order.CreatedAt,
            order.Total,
            order.Items.Select(i => new OrderItemResponseDto(
                i.ProductId,
                products.TryGetValue(i.ProductId, out var prod) ? prod.Name : string.Empty,
                i.Quantity,
                i.Price
            )).ToList()
        );

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Validate products
        var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Count)
            return BadRequest("One or more products do not exist.");

        var order = new Order
        {
            UserId = dto.UserId,
            Items = dto.Items.Select(i =>
            {
                var prod = products.First(p => p.Id == i.ProductId);
                return new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    Price = prod.Price
                };
            }).ToList()
        };

        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, new { order.Id });
    }
}
