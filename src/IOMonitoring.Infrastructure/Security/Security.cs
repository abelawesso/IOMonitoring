using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IOMonitoring.Application.Abstractions;
using IOMonitoring.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IOMonitoring.Infrastructure.Security;

/// <summary>Claim names written into the JWT and read back by the API and the Blazor client.</summary>
public static class AppClaims
{
    public const string Subject = "sub";
    public const string Username = "unique_name";
    public const string DisplayName = "display_name";
    public const string Role = "role";
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "IOMonitoring.Api";
    public string Audience { get; set; } = "IOMonitoring.Web";

    /// <summary>HMAC-SHA256 key; must be at least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 120;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider time) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(AppUser user)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(AppClaims.Subject, user.Id.ToString()),
            new Claim(AppClaims.Username, user.Username),
            new Claim(AppClaims.DisplayName, user.DisplayName),
            new Claim(AppClaims.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear(); // keep the short claim names above as-is
        return (handler.WriteToken(token), expires);
    }
}

/// <summary>PBKDF2-SHA256 hasher. Format: {iterations}.{saltBase64}.{hashBase64}</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
