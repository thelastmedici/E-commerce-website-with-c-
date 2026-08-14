public class OrderItem // public class that shows schema of a single Item within an Order
{
    public int Id { get; set; }
    public int ProductId { get; set; } //refences which product is in the order(foreign key to product)
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
