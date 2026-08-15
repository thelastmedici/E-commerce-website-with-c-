public class Order // public class schema for Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; //add  timeStamp to  when the particular oder was created 
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>(); // this makes a list of user Order

    public decimal Total => Items.Sum(i => i.Price * i.Quantity);
}
