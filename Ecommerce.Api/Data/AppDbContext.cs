using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>()
            .Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        var rowVersion = modelBuilder.Entity<Product>().Property(p => p.RowVersion);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            rowVersion
                .IsConcurrencyToken()
                .ValueGeneratedOnAdd()
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");
        }
        else
        {
            rowVersion.IsRowVersion();
        }

        var orderRowVersion = modelBuilder.Entity<Order>().Property(o => o.RowVersion);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            orderRowVersion
                .IsConcurrencyToken()
                .ValueGeneratedOnAdd()
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");
        }
        else
        {
            orderRowVersion.IsRowVersion();
        }

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(OrderStatus.Pending);

        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        modelBuilder.Entity<User>()
            .Property(u => u.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(320);

        modelBuilder.Entity<User>()
            .HasMany(u => u.Orders)
            .WithOne(o => o.User)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique();

        modelBuilder.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
