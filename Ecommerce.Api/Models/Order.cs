public class Order // public class schema for Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; //add  timeStamp to  when the particular oder was created 
    public List<OrderItem> Items { get; set; } // this makes a list of user Order
}
