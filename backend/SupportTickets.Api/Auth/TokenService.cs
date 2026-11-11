using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SupportTickets.Api.Auth;

/// <summary>Issues the admin's JWT (HS256, short-lived, role "Admin").</summary>
public sealed class TokenService(JwtOptions options, TimeProvider time)
{
    public const string AdminRole = "Admin";

    public (string Token, DateTimeOffset ExpiresAt) CreateAdminToken(string username)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(options.LifetimeMinutes);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity([new Claim("name", username), new Claim("role", AdminRole)]),
            SigningCredentials = new SigningCredentials(SigningKey(options), SecurityAlgorithms.HmacSha256),
        });

        return (token, expires);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey!));
}
