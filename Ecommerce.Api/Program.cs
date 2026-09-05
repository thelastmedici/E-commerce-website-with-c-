using System.Threading.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddDebug();

var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(section => section.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Cast<string>()
    .ToArray();

if (allowedOrigins.Length == 0)
    throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one origin.");

var rateLimitPermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100);
var rateLimitWindowSeconds = builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60);
if (rateLimitPermitLimit < 1 || rateLimitWindowSeconds < 1)
    throw new InvalidOperationException("RateLimiting:PermitLimit and RateLimiting:WindowSeconds must be positive.");

var trustForwardedHeaders = builder.Configuration.GetValue("ForwardedHeaders:TrustAll", false);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    if (trustForwardedHeaders)
    {
        // Only enable this behind a private, trusted reverse-proxy network.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = rateLimitWindowSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too many requests.", status = StatusCodes.Status429TooManyRequests },
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");

        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ecommerce API",
        Version = "v1",
        Description = "E-commerce backend API with authentication, products, and orders."
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled exception. Method: {Method}. Path: {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var response = new
        {
            title = "An unexpected error occurred.",
            status = StatusCodes.Status500InternalServerError,
            detail = app.Environment.IsDevelopment() && exception is not null ? exception.Message : "Internal server error."
        };

        await context.Response.WriteAsJsonAsync(response);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ecommerce API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var healthCheckOptions = new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
};
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = healthCheckOptions.ResponseWriter
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", healthCheckOptions).AllowAnonymous();
app.MapControllers();

await AdminBootstrapper.ProvisionAsync(app.Services, builder.Configuration);

app.Run();

public partial class Program { }
