public class Product // this is a public class that defines the product schema
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; } = 0;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
