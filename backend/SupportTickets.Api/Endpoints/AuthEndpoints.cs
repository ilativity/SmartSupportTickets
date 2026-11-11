using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using SupportTickets.Api.Auth;
using SupportTickets.Api.Validation;

namespace SupportTickets.Api.Endpoints;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(100)]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(200)]
    public string? Password { get; set; }
}

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Username);

public static class AuthEndpoints
{
    /// <summary>Name of the rate-limiter policy on login (configured in Program).</summary>
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", Login)
            .WithTags("Auth")
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting(LoginRateLimitPolicy);
        return app;
    }

    private static Results<Ok<LoginResponse>, ProblemHttpResult> Login(
        LoginRequest request, AdminCredentialsVerifier verifier, TokenService tokens)
    {
        if (!verifier.Verify(request.Username!, request.Password!))
        {
            // One message for "no such user" and "wrong password": telling them apart would confirm usernames.
            return TypedResults.Problem("Invalid username or password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var (token, expiresAt) = tokens.CreateAdminToken(request.Username!);
        return TypedResults.Ok(new LoginResponse(token, expiresAt, request.Username!));
    }
}
