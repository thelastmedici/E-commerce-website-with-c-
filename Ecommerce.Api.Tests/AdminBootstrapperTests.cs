using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class AdminBootstrapperTests
{
    [Fact]
    public async Task ProvisionAsync_RejectsPartialConfiguration()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = "admin@example.com"
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminBootstrapper.ProvisionAsync(services, configuration));

        Assert.Contains("must be configured together", exception.Message);
    }

    [Fact]
    public async Task ProvisionAsync_CreatesAdminAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(options => options.UseSqlite(connection))
            .BuildServiceProvider();

        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        }

        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = " Admin@Example.com ",
            ["AdminBootstrap:Password"] = "A-secure-bootstrap-password"
        });

        await AdminBootstrapper.ProvisionAsync(services, configuration);
        await AdminBootstrapper.ProvisionAsync(services, configuration);

        using var verificationScope = services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admins = await db.Users.Where(user => user.Role == "Admin").ToListAsync();

        var admin = Assert.Single(admins);
        Assert.Equal("Admin@Example.com", admin.Email);
        Assert.Equal("ADMIN@EXAMPLE.COM", admin.NormalizedEmail);
        Assert.True(BCrypt.Net.BCrypt.Verify("A-secure-bootstrap-password", admin.PasswordHash));
    }

    [Fact]
    public async Task ProvisionAsync_RefusesToPromoteExistingUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(options => options.UseSqlite(connection))
            .BuildServiceProvider();

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                Email = "admin@example.com",
                NormalizedEmail = "ADMIN@EXAMPLE.COM",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                Role = "User"
            });
            await db.SaveChangesAsync();
        }

        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = "admin@example.com",
            ["AdminBootstrap:Password"] = "A-secure-bootstrap-password"
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminBootstrapper.ProvisionAsync(services, configuration));

        Assert.Contains("Refusing to change", exception.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
