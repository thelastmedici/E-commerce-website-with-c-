using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keeperConnection = new(
        $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public TestApplicationFactory()
    {
        SetEnvironment("Jwt__Key", "test-only-key-that-is-at-least-32-characters-long");
        SetEnvironment("ConnectionStrings__DefaultConnection", "Data Source=test-only");
        SetEnvironment("Cors__AllowedOrigins__0", "http://localhost");
        _keeperConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_keeperConnection.ConnectionString));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // SQLite shared-memory databases do not reliably drop through EnsureDeleted.
        // Clear dependent rows explicitly so each test starts from a known state.
        db.OrderItems.RemoveRange(await db.OrderItems.ToListAsync());
        db.Orders.RemoveRange(await db.Orders.ToListAsync());
        db.Products.RemoveRange(await db.Products.ToListAsync());
        db.Users.RemoveRange(await db.Users.ToListAsync());
        await db.SaveChangesAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _keeperConnection.DisposeAsync();

        foreach (var (key, value) in _originalEnvironment)
            Environment.SetEnvironmentVariable(key, value);
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
