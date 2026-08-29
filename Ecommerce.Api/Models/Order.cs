public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled,
    Refunded
}

public class Order // public class schema for Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; //add  timeStamp to  when the particular oder was created 
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>(); // this makes a list of user Order
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime? CancelledAt { get; set; }
    public DateTime? RefundedAt { get; set; }

    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public decimal Total => Items.Sum(i => i.Price * i.Quantity);
}
