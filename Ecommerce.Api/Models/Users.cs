public class User // public class that define each user account schema
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}