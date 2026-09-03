using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class AdminBootstrapper
{
    private const int MinimumBootstrapPasswordLength = 12;

    public static async Task ProvisionAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var email = configuration["AdminBootstrap:Email"]?.Trim();
        var password = configuration["AdminBootstrap:Password"];

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            return;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "AdminBootstrap:Email and AdminBootstrap:Password must be configured together.");

        if (!new EmailAddressAttribute().IsValid(email) || email.Length > 320)
            throw new InvalidOperationException("AdminBootstrap:Email must be a valid email address.");

        if (password.Length < MinimumBootstrapPasswordLength)
            throw new InvalidOperationException(
                $"AdminBootstrap:Password must be at least {MinimumBootstrapPasswordLength} characters.");

        var normalizedEmail = email.ToUpperInvariant();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AdminBootstrapper");
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (user is not null)
        {
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "AdminBootstrap:Email belongs to a non-admin user. Refusing to change the existing user's role.");

            logger.LogInformation("Admin bootstrap account already exists for {Email}.", email);
            return;
        }

        db.Users.Add(new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "Admin"
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Admin bootstrap account created for {Email}.", email);
        }
        catch (DbUpdateException exception) when (IsEmailUniquenessViolation(exception))
        {
            // Another application instance may have created the same bootstrap account first.
            db.ChangeTracker.Clear();
            var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.NormalizedEmail == normalizedEmail,
                cancellationToken);

            if (existing is null || !string.Equals(existing.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "AdminBootstrap:Email belongs to a non-admin user. Refusing to change the existing user's role.",
                    exception);

            logger.LogInformation("Admin bootstrap account already exists for {Email}.", email);
        }
    }

    private static bool IsEmailUniquenessViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("IX_Users_NormalizedEmail", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed: Users.NormalizedEmail", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
