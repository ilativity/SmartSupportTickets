namespace SupportTickets.Api.Auth;

/// <summary>JWT settings (<c>Jwt:*</c>). The signing key is a secret: user-secrets or an environment variable only.</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "SmartSupportTickets";
    public string Audience { get; set; } = "SmartSupportTickets";
    public string? SigningKey { get; set; }
    public int LifetimeMinutes { get; set; } = 60;
}

/// <summary>
/// The admin account (<c>Auth:Admin:*</c>). Only a password <b>hash</b> is configured, made with
/// <c>dotnet run -- hash-password &lt;password&gt;</c>; the password itself is never stored anywhere.
/// </summary>
public sealed class AdminOptions
{
    public string Username { get; set; } = "admin";
    public string? PasswordHash { get; set; }
}

/// <summary>
/// Secrets policy (D13). Development and E2E get built-in, publicly known defaults so the app runs with zero setup;
/// every other environment must provide real values or the app refuses to start. A missing secret must never
/// silently fall back to a key that is printed in a public repository.
/// </summary>
public static class AuthSecrets
{
    /// <summary>Known to everyone who reads this repository — which is exactly why it is allowed only in Development/E2E.</summary>
    public const string DevelopmentSigningKey = "development-only-signing-key--never-use-in-production--0123456789";

    public const string DevelopmentAdminPassword = "Admin123!";

    /// <summary>HS256 needs at least a 256-bit key.</summary>
    public const int MinimumKeyLength = 32;

    public static bool AllowsDefaults(IHostEnvironment env) => env.IsDevelopment() || env.IsEnvironment("E2E");

    /// <summary>Fills in development defaults where allowed, otherwise throws if anything is missing or weak.</summary>
    public static void Apply(JwtOptions jwt, AdminOptions admin, IHostEnvironment env)
    {
        if (AllowsDefaults(env))
        {
            // Empty counts as missing: docker compose passes "" for an unset variable, and "??=" would keep that "".
            if (string.IsNullOrWhiteSpace(jwt.SigningKey)) jwt.SigningKey = DevelopmentSigningKey;
            if (string.IsNullOrWhiteSpace(admin.PasswordHash)) admin.PasswordHash = PasswordHashing.Hash(DevelopmentAdminPassword);
        }

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey is not configured for environment '{env.EnvironmentName}'. Refusing to start (see README, Configuration).");
        }

        if (jwt.SigningKey.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException($"Jwt:SigningKey must be at least {MinimumKeyLength} characters.");
        }

        // "Contains", not "equals": the public key wrapped in spaces or quotes is just as public.
        if (!AllowsDefaults(env) && jwt.SigningKey.Contains(DevelopmentSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The public development signing key cannot be used outside Development/E2E.");
        }

        if (jwt.LifetimeMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Jwt:LifetimeMinutes must be between 1 and 1440.");
        }

        if (string.IsNullOrWhiteSpace(admin.PasswordHash))
        {
            throw new InvalidOperationException(
                $"Auth:Admin:PasswordHash is not configured for environment '{env.EnvironmentName}'. Refusing to start.");
        }

        // A truncated or mistyped hash would otherwise start fine and then silently refuse every login.
        if (!PasswordHashing.LooksValid(admin.PasswordHash))
        {
            throw new InvalidOperationException(
                "Auth:Admin:PasswordHash is not a valid hash. Create one with: dotnet run -- hash-password");
        }
    }
}
