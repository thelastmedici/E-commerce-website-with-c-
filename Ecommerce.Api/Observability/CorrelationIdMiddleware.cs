using System.Diagnostics;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetCorrelationId(context);
        context.Response.Headers[HeaderName] = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        var requestedId = context.Request.Headers[HeaderName].FirstOrDefault();
        return Guid.TryParse(requestedId, out var parsedId)
            ? parsedId.ToString("D")
            : Guid.NewGuid().ToString("D");
    }
}
