using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ApiTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public ApiTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterAndLogin_ReturnsUsableJwt()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "customer@example.com",
            password = "Password123!"
        });
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "customer@example.com",
            password = "Password123!"
        });
        var token = await ReadTokenAsync(login);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var products = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_RejectMissingJwt()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProductManagement_RequiresAdminRole()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client, "customer@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Unauthorized product",
            price = 10,
            stock = 5
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProductValidation_RejectsNegativePrice()
    {
        await _factory.ResetDatabaseAsync();
        await SeedUserAsync("admin@example.com", "Admin");
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, "admin@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Invalid product",
            price = -1,
            stock = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrderCreation_RejectsQuantityAboveStock()
    {
        await _factory.ResetDatabaseAsync();
        var productId = await SeedProductAsync("Limited product", 10, 1);
        using var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client, "customer@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId, quantity = 2 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Orders_AreVisibleOnlyToTheirOwner()
    {
        await _factory.ResetDatabaseAsync();
        var productId = await SeedProductAsync("Owned product", 10, 2);
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient, "owner@example.com");
        var otherToken = await RegisterAndLoginAsync(otherClient, "other@example.com");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var create = await ownerClient.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });
        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var collection = await otherClient.GetAsync("/api/orders");
        var details = await otherClient.GetAsync($"/api/orders/{orderId}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Empty(await collection.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOrders_CannotOversellStock()
    {
        await _factory.ResetDatabaseAsync();
        var productId = await SeedProductAsync("Concurrent product", 10, 1);
        using var firstClient = _factory.CreateClient();
        using var secondClient = _factory.CreateClient();
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAndLoginAsync(firstClient, "first@example.com"));
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAndLoginAsync(secondClient, "second@example.com"));

        var responses = await Task.WhenAll(
            PlaceOrderAsync(firstClient, productId),
            PlaceOrderAsync(secondClient, productId));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, response =>
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
    }

    private async Task<string> RegisterAndLoginAsync(HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Password123!"
        });
        register.EnsureSuccessStatusCode();
        return await LoginAsync(client, email);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Password123!"
        });
        response.EnsureSuccessStatusCode();
        return await ReadTokenAsync(response);
    }

    private static async Task<string> ReadTokenAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private async Task SeedUserAsync(string email, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = role
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedProductAsync(string name, decimal price, int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product { Name = name, Price = price, Stock = stock };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static Task<HttpResponseMessage> PlaceOrderAsync(HttpClient client, int productId) =>
        client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });
}
