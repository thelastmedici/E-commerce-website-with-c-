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

        var result = orders.Select(o => ToResponse(o, products));

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

        var dto = ToResponse(order, products);

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

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] OrderStatusUpdateDto dto)
    {
        if (!Enum.IsDefined(dto.Status))
            return BadRequest(new { error = "Invalid order status." });

        var order = await _db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status == dto.Status) return Ok(order.Status);

        if (!IsValidStatusTransition(order.Status, dto.Status))
            return Conflict(new { error = $"Order cannot move from {order.Status} to {dto.Status}." });

        order.Status = dto.Status;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The order was changed by another request. Please retry." });
        }

        return Ok(order.Status);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            IQueryable<Order> orderQuery = _db.Orders
                .Include(o => o.Items)
                .Where(o => o.Id == id);

            if (!User.IsInRole("Admin"))
                orderQuery = orderQuery.Where(o => o.UserId == userId.Value);

            var order = await orderQuery.FirstOrDefaultAsync();
            if (order is null) return NotFound();

            if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
                return Conflict(new { error = "The order has already been closed." });

            if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered)
                return Conflict(new { error = "Orders cannot be cancelled after shipment." });

            var productIds = order.Items.Select(item => item.ProductId).Distinct().ToList();
            var products = await _db.Products
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id);

            foreach (var item in order.Items)
                products[item.ProductId].Stock += item.Quantity;

            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(ToResponse(order, products));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { error = "The order or its stock changed. Please retry the cancellation." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Refund(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order is null) return NotFound();

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
            return Conflict(new { error = "The order has already been closed." });

        if (order.Status is OrderStatus.Pending or OrderStatus.Confirmed)
            return Conflict(new { error = "An order must be shipped or delivered before it can be refunded." });

        order.Status = OrderStatus.Refunded;
        order.RefundedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The order was changed by another request. Please retry." });
        }

        return Ok(new
        {
            message = "Refund recorded. Connect a payment provider to transfer funds.",
            order.Id,
            order.Status,
            order.RefundedAt
        });
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(claim, out var userId) ? userId : null;
    }

    private static bool IsValidStatusTransition(OrderStatus current, OrderStatus next) =>
        (current, next) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false
        };

    private static OrderResponseDto ToResponse(
        Order order,
        IReadOnlyDictionary<int, Product> products) => new(
            order.Id,
            order.UserId,
            order.CreatedAt,
            order.Total,
            order.Status,
            order.CancelledAt,
            order.RefundedAt,
            order.Items.Select(item => new OrderItemResponseDto(
                item.ProductId,
                products.TryGetValue(item.ProductId, out var product) ? product.Name : string.Empty,
                item.Quantity,
                item.Price
            )).ToList());
}
