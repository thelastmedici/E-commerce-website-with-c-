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

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }

    [Fact]
    public async Task Registration_NormalizesEmailAndRejectsDuplicates()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "Customer@Example.com",
            password = "Password123!"
        });
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "customer@example.com",
            password = "Password123!"
        });
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "customer@example.com",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task AuthenticationValidation_RejectsInvalidEmailAndWeakPassword()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "not-an-email",
            password = "weak"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task HealthEndpoints_ArePublicAndReturnCorrelationId()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.True(live.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.True(Guid.TryParse(values.Single(), out _));
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
    public async Task ProductValidation_RejectsBlankName()
    {
        await _factory.ResetDatabaseAsync();
        await SeedUserAsync("admin@example.com", "Admin");
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, "admin@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "   ",
            price = 10,
            stock = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProductListing_SupportsSearchFiltersAndPagination()
    {
        await _factory.ResetDatabaseAsync();
        await SeedProductAsync("Wireless phone", 100, 4);
        await SeedProductAsync("Phone case", 20, 0);
        await SeedProductAsync("Laptop stand", 50, 3);
        using var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client, "catalog@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/products?search=phone&minPrice=50&inStock=true&page=1&pageSize=1");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("totalPages").GetInt32());
        Assert.Single(body.GetProperty("items").EnumerateArray());
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
    public async Task OrderCancellation_RestoresStockForTheOwner()
    {
        await _factory.ResetDatabaseAsync();
        var productId = await SeedProductAsync("Cancelable product", 10, 2);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAndLoginAsync(client, "cancel@example.com"));

        var create = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });
        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var cancel = await client.PostAsync($"/api/orders/{orderId}/cancel", null);
        var productResponse = await client.GetAsync($"/api/products/{productId}");
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(2, product.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Admin_CanAdvanceOrderAndRecordRefund()
    {
        await _factory.ResetDatabaseAsync();
        var productId = await SeedProductAsync("Refundable product", 10, 1);
        await SeedUserAsync("admin@example.com", "Admin");
        using var customerClient = _factory.CreateClient();
        using var adminClient = _factory.CreateClient();
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAndLoginAsync(customerClient, "buyer@example.com"));
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(adminClient, "admin@example.com"));

        var create = await customerClient.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });
        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await SetOrderStatusAsync(adminClient, orderId, "Confirmed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetOrderStatusAsync(adminClient, orderId, "Shipped")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetOrderStatusAsync(adminClient, orderId, "Delivered")).StatusCode);
        var refund = await adminClient.PostAsync($"/api/orders/{orderId}/refund", null);
        var refundBody = await refund.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Equal("Refunded", refundBody.GetProperty("status").GetString());
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
            Email = email.Trim().ToUpperInvariant(),
            NormalizedEmail = email.Trim().ToUpperInvariant(),
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

    private static Task<HttpResponseMessage> SetOrderStatusAsync(HttpClient client, int orderId, string status) =>
        client.PatchAsJsonAsync($"/api/orders/{orderId}/status", new { status });
}
