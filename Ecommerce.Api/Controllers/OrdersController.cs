using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var products = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        IQueryable<Order> ordersQuery = _db.Orders
            .Include(o => o.Items)
            .AsNoTracking();

        if (!User.IsInRole("Admin"))
            ordersQuery = ordersQuery.Where(o => o.UserId == userId.Value);

        var orders = await ordersQuery.ToListAsync();

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
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        IQueryable<Order> orderQuery = _db.Orders.Include(o => o.Items).Where(o => o.Id == id);
        if (!User.IsInRole("Admin"))
            orderQuery = orderQuery.Where(o => o.UserId == userId.Value);

        var order = await orderQuery.FirstOrDefaultAsync();
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

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        // Validate product IDs and start a transaction for atomicity
        var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
            if (products.Count != productIds.Count)
                return BadRequest("One or more products do not exist.");

            // Check stock availability
            foreach (var item in dto.Items)
            {
                var prod = products.First(p => p.Id == item.ProductId);
                if (prod.Stock < item.Quantity)
                    return BadRequest($"Insufficient stock for product '{prod.Name}' (id={prod.Id}). Available: {prod.Stock}, requested: {item.Quantity}.");
            }

            // Build order and decrement stock
            var order = new Order
            {
                // Ownership always comes from the authenticated identity, never the request body.
                UserId = userId.Value,
                Items = dto.Items.Select(i =>
                {
                    var prod = products.First(p => p.Id == i.ProductId);
                    // decrement stock in the tracked entity
                    prod.Stock -= i.Quantity;
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

            await transaction.CommitAsync();

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, new { order.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { error = "A concurrency conflict occurred while updating product stock. Please retry the order." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(claim, out var userId) ? userId : null;
    }
}
