using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var normalized = NormalizeEmail(dto.Email);
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normalized))
            return Conflict(new { error = "An account with this email already exists." });

        var user = new User
        {
            Email = dto.Email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsEmailUniquenessViolation(exception))
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        return StatusCode(StatusCodes.Status201Created, new { message = "Account created." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var normalized = NormalizeEmail(dto.Email);
        var user = await _db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password." });

        var token = GenerateJwtToken(user);
        return Ok(new { token });
    }

    private string GenerateJwtToken(User user)
    {
        var configuredKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key must be configured.");
        var key = Encoding.UTF8.GetBytes(configuredKey);
        var issuer = _configuration["Jwt:Issuer"] ?? "EcommerceApi";
        var audience = _configuration["Jwt:Audience"] ?? "EcommerceApiUsers";
        var expireMinutes = int.TryParse(_configuration["Jwt:ExpireMinutes"], out var minutes) ? minutes : 60;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsEmailUniquenessViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("IX_Users_NormalizedEmail", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed: Users.NormalizedEmail", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
