public class OrderItem // public class that shows schema of a single Item within an Order
{
    public int Id { get; set; }
    public int ProductId { get; set; } //refences which product is in the order(foreign key to product)
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public int OrderId { get; set; } // foreign key back to Order
    public Order Order { get; set; } = null!;
}
