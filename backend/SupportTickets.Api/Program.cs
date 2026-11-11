// Composition root (wires ports to adapters) and the HTTP driving adapter. Wiring only — no business logic here.
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SupportTickets.Adapters;
using SupportTickets.Api.Auth;
using SupportTickets.Api.Endpoints;
using SupportTickets.Api.Tools;
using SupportTickets.Core.Application;
using SupportTickets.Core.Ports.In;

// `dotnet run -- hash-password <pwd>` prints a hash and exits; any other arguments start the web app.
if (HashPasswordCommand.TryRun(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var frontendBaseUrl = new Uri(config["App:FrontendBaseUrl"]
    ?? throw new InvalidOperationException("App:FrontendBaseUrl is not configured."));

// --- Core (the hexagon) and its driven adapters ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new NotificationComposer(frontendBaseUrl));
builder.Services.AddScoped<ITicketUseCases, TicketUseCases>();
builder.Services.AddAdapters(config, builder.Environment.ContentRootPath);

// --- Authentication: one admin account, JWT bearer tokens (D3, D13) ---
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var admin = config.GetSection("Auth:Admin").Get<AdminOptions>() ?? new AdminOptions();
AuthSecrets.Apply(jwt, admin, builder.Environment); // throws — app does not start — if secrets are missing
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(admin);
builder.Services.AddSingleton<AdminCredentialsVerifier>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false; // keep "role"/"name" as issued, not renamed to long XML-schema URIs
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = TokenService.SigningKey(jwt),
        // Only the algorithm we issue. Unsigned ("alg":"none") tokens are refused by the library anyway; pinning
        // the algorithm also refuses tokens signed in any other way, so what counts as valid is exactly what we issue.
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = "role",
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(TicketEndpoints.AdminOnlyPolicy, policy => policy.RequireRole(TokenService.AdminRole));

// Login attempts per client IP per minute. Configurable because the end-to-end test suite logs in many times from
// one address; it raises the limit in the E2E environment only.
var loginLimit = config.GetValue("Auth:LoginRateLimit:PermitLimit", 5);
// A second, shared ceiling on all login attempts together: the per-IP limit alone does not stop a guesser who rotates
// through many addresses. Legitimate use (one admin) never comes near it.
var loginGlobalLimit = config.GetValue("Auth:LoginRateLimit:GlobalPermitLimit", 30);
// Anonymous ticket creation (each may carry a 5 MB image) per client IP per minute: a script must not be able to fill
// the disk or the inbox. Generous for people, raised in the E2E environment.
var createLimit = config.GetValue("Tickets:CreateRateLimit:PermitLimit", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
        {
            HttpContext = context.HttpContext,
            ProblemDetails = { Status = 429, Title = "Too many requests", Detail = "Too many attempts. Try again in a minute." },
        });
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        http.Request.Path.StartsWithSegments("/api/auth/login")
            ? RateLimitPartition.GetFixedWindowLimiter("all-logins",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = loginGlobalLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    options.AddPolicy(TicketEndpoints.CreateRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = createLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Behind a reverse proxy (nginx in Docker) every request arrives from the proxy's address, so per-IP limits would
// count all clients as one. X-Forwarded-For is honoured ONLY from the configured proxy network — trusting it from
// anyone would let a client pick a new "IP" per request and escape the limits.
var trustedProxyNetwork = config["ReverseProxy:TrustedNetwork"];
if (!string.IsNullOrWhiteSpace(trustedProxyNetwork))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(trustedProxyNetwork));
        options.ForwardLimit = 1; // only the address our own proxy wrote
    });
}

// --- HTTP concerns ---
// Every error, including unhandled exceptions and bare 401/403/404, is an RFC 7807 ProblemDetails JSON body,
// so the frontend has one error shape to handle.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    // Only the frontend's own origin; in development and Docker the frontend proxies /api, so CORS is a fallback
    // for running the two on different hosts.
    .WithOrigins(frontendBaseUrl.GetLeftPart(UriPartial.Authority))
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(trustedProxyNetwork))
{
    app.UseForwardedHeaders(); // first: everything after it (rate limits, logs) sees the real client address
}

// CORS first, so that every response — errors and 429s included — carries CORS headers a cross-origin frontend
// needs in order to read it (otherwise "too many attempts" would surface as an unreadable network error).
app.UseCors();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A malformed request (no form body, body too large) is the client's error and carries its own status code
    // (400, 413); without this it would be reported as a 500, telling the client it was the server's fault.
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(); // interactive API docs at /scalar/v1
}

app.MapHealthChecks("/health");
app.MapTicketEndpoints();
app.MapAuthEndpoints();
app.MapUploadEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> in the test project can host the API in memory.
public partial class Program;
