using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SupportTickets.Api.Auth;
using SupportTickets.Api.Tools;

namespace SupportTickets.Tests.Api;

public sealed class AuthTests : IDisposable
{
    private const string SeedId = "b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1"; // John Doe, New, no resolution

    private readonly ApiFactory _api = new();
    private readonly HttpClient _client;

    public AuthTests() => _client = _api.CreateClient();

    public void Dispose() => _api.Dispose();

    private Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { username, password });

    private async Task<string> AdminTokenAsync()
    {
        var response = await LoginAsync("admin", AuthSecrets.DevelopmentAdminPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["token"]!;
    }

    private Task<HttpResponseMessage> PutAsync(string? token, object body, string id = SeedId)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tickets/{id}") { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request);
    }

    private static string Token(string key, DateTime expires, string role = "Admin", string issuer = "SmartSupportTickets",
        string algorithm = SecurityAlgorithms.HmacSha256, string audience = "SmartSupportTickets") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = expires.AddHours(-2),
            IssuedAt = expires.AddHours(-2),
            Expires = expires,
            Subject = new ClaimsIdentity([new Claim("name", "admin"), new Claim("role", role)]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm),
        });

    // ---- login ----

    [Fact]
    public async Task Login_with_the_right_password_returns_a_token_that_expires_in_an_hour()
    {
        var response = await LoginAsync("admin", AuthSecrets.DevelopmentAdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.False(string.IsNullOrEmpty((string?)body["token"]));
        var expires = DateTimeOffset.Parse((string)body["expiresAt"]!);
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_401()
    {
        var wrongPassword = await LoginAsync("admin", "nope");
        var unknownUser = await LoginAsync("mallory", AuthSecrets.DevelopmentAdminPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(
            (string?)JsonNode.Parse(await wrongPassword.Content.ReadAsStringAsync())!["detail"],
            (string?)JsonNode.Parse(await unknownUser.Content.ReadAsStringAsync())!["detail"]);
    }

    [Fact]
    public async Task Login_with_missing_fields_is_a_validation_problem() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin" })).StatusCode);

    [Fact]
    public async Task Too_many_login_attempts_get_429_not_401()
    {
        using var api = new ApiFactory { Settings = { ["Auth:LoginRateLimit:PermitLimit"] = "2" } };
        var client = api.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "x" })).StatusCode);
        }

        // Even the right password is refused now: the point is to stop guessing, not to judge the guess.
        var third = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = AuthSecrets.DevelopmentAdminPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.True(third.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task All_login_attempts_together_have_a_ceiling_too()
    {
        // Per-IP limit high, shared limit low: the shared one must still stop the third attempt.
        using var api = new ApiFactory
        {
            Settings = { ["Auth:LoginRateLimit:PermitLimit"] = "100", ["Auth:LoginRateLimit:GlobalPermitLimit"] = "2" },
        };
        var client = api.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "x" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "x" })).StatusCode);
        // Other endpoints are not affected by the login ceiling.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tickets")).StatusCode);
    }

    // ---- editing requires an admin token ----

    [Fact]
    public async Task Anonymous_update_is_401_and_changes_nothing()
    {
        var response = await PutAsync(null, new { status = "Closed", resolution = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var ticket = await _client.GetFromJsonAsync<JsonObject>($"/api/tickets/{SeedId}");
        Assert.Equal("New", (string)ticket!["status"]!);
        Assert.Empty(_api.Mail.Outbox);
    }

    public static TheoryData<string, string> BadTokens => new()
    {
        { "garbage", "not-a-jwt" },
        { "signed with another key", Token("another-key-another-key-another-key-0123456789", DateTime.UtcNow.AddHours(1)) },
        { "expired", Token(AuthSecrets.DevelopmentSigningKey, DateTime.UtcNow.AddMinutes(-5)) },
        { "right key, other algorithm", Token(AuthSecrets.DevelopmentSigningKey, DateTime.UtcNow.AddHours(1), algorithm: SecurityAlgorithms.HmacSha384) },
        { "wrong audience", Token(AuthSecrets.DevelopmentSigningKey, DateTime.UtcNow.AddHours(1), audience: "someone-else") },
        { "wrong issuer", Token(AuthSecrets.DevelopmentSigningKey, DateTime.UtcNow.AddHours(1), issuer: "evil") },
        // {"alg":"none"} with role Admin and no signature: must never be accepted.
        { "alg none", "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJpc3MiOiJTbWFydFN1cHBvcnRUaWNrZXRzIiwiYXVkIjoiU21hcnRTdXBwb3J0VGlja2V0cyIsInJvbGUiOiJBZG1pbiIsImV4cCI6NDEwMjQ0NDgwMH0." },
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Update_with_an_invalid_token_is_401(string why, string token)
    {
        var response = await PutAsync(token, new { status = "Closed", resolution = "" });

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{why}: {response.StatusCode}");
    }

    [Fact]
    public async Task A_valid_token_without_the_admin_role_is_403()
    {
        var token = Token(AuthSecrets.DevelopmentSigningKey, DateTime.UtcNow.AddHours(1), role: "Customer");

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(token, new { status = "Closed", resolution = "" })).StatusCode);
    }

    [Fact]
    public async Task Admin_update_returns_200_with_the_full_email_and_sends_one_email()
    {
        var token = await AdminTokenAsync();

        var response = await PutAsync(token, new { status = "In Progress", resolution = "  Technician booked  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("In Progress", (string)ticket["status"]!);
        Assert.Equal("Technician booked", (string)ticket["resolution"]!);
        Assert.Equal("john.doe@example.com", (string)ticket["email"]!);
        var mail = Assert.Single(_api.Mail.Outbox);
        Assert.Contains("New -> In Progress", mail.TextBody);
        Assert.Contains("Technician booked", mail.TextBody);
    }

    [Fact]
    public async Task Saving_without_changes_sends_no_email()
    {
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(token, new { status = "New", resolution = "" })).StatusCode);
        Assert.Empty(_api.Mail.Outbox);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("2")]
    [InlineData("")]
    public async Task Update_with_an_unknown_status_is_400(string status)
    {
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(token, new { status, resolution = "" })).StatusCode);
    }

    [Fact]
    public async Task Update_with_an_overlong_resolution_is_400()
    {
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(token, new { status = "New", resolution = new string('r', 2001) })).StatusCode);
    }

    [Fact]
    public async Task Update_of_an_unknown_ticket_is_404()
    {
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(token, new { status = "New", resolution = "" }, Guid.NewGuid().ToString())).StatusCode);
    }

    [Fact]
    public async Task Admin_sees_the_full_email_in_details_and_still_none_in_the_list()
    {
        var token = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var details = await _client.GetFromJsonAsync<JsonObject>($"/api/tickets/{SeedId}");
        var list = await _client.GetStringAsync("/api/tickets");

        Assert.Equal("john.doe@example.com", (string)details!["email"]!);
        Assert.DoesNotContain("@", list);
    }

    [Fact]
    public async Task An_invalid_token_on_a_public_read_is_treated_as_anonymous()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var details = await _client.GetFromJsonAsync<JsonObject>($"/api/tickets/{SeedId}");

        Assert.Equal("j***@example.com", (string)details!["email"]!);
    }
}

/// <summary>D13: outside Development/E2E the app must refuse to start without real secrets.</summary>
public class SecretsPolicyTests
{
    private const string StrongKey = "production-test-key-production-test-key-0123456789";

    [Fact]
    public void Production_without_a_signing_key_does_not_start()
    {
        using var api = new ApiFactory { Environment = "Production", Settings = { ["Auth:Admin:PasswordHash"] = PasswordHashing.Hash("x") } };

        var ex = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains("Jwt:SigningKey", ex.ToString());
    }

    [Fact]
    public void Production_without_an_admin_password_hash_does_not_start()
    {
        using var api = new ApiFactory { Environment = "Production", Settings = { ["Jwt:SigningKey"] = StrongKey } };

        var ex = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains("Auth:Admin:PasswordHash", ex.ToString());
    }

    [Fact]
    public void Production_with_the_public_development_key_does_not_start()
    {
        using var api = new ApiFactory
        {
            Environment = "Production",
            // Wrapped in spaces: still the public key.
            Settings = { ["Jwt:SigningKey"] = $"  {AuthSecrets.DevelopmentSigningKey}  ", ["Auth:Admin:PasswordHash"] = PasswordHashing.Hash("x") },
        };

        Assert.Contains("public development signing key", Assert.ThrowsAny<Exception>(() => api.CreateClient()).ToString());
    }

    [Fact]
    public void Staging_gets_no_defaults_either()
    {
        using var api = new ApiFactory { Environment = "Staging" };

        Assert.Contains("Jwt:SigningKey", Assert.ThrowsAny<Exception>(() => api.CreateClient()).ToString());
    }

    [Fact]
    public async Task The_E2E_environment_gets_the_development_defaults()
    {
        using var api = new ApiFactory { Environment = "E2E" };

        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = AuthSecrets.DevelopmentAdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_with_empty_secret_variables_uses_the_defaults()
    {
        // What docker compose passes for variables that are not set.
        using var api = new ApiFactory { Settings = { ["Jwt:SigningKey"] = "", ["Auth:Admin:PasswordHash"] = "" } };

        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = AuthSecrets.DevelopmentAdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_malformed_password_hash_stops_startup()
    {
        using var api = new ApiFactory
        {
            Environment = "Production",
            Settings = { ["Jwt:SigningKey"] = StrongKey, ["Auth:Admin:PasswordHash"] = PasswordHashing.Hash("x")[..20] },
        };

        Assert.Contains("not a valid hash", Assert.ThrowsAny<Exception>(() => api.CreateClient()).ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100000")]
    public void A_token_lifetime_out_of_range_stops_startup(string minutes)
    {
        using var api = new ApiFactory { Settings = { ["Jwt:LifetimeMinutes"] = minutes } };

        Assert.Contains("LifetimeMinutes", Assert.ThrowsAny<Exception>(() => api.CreateClient()).ToString());
    }

    [Fact]
    public void A_short_signing_key_is_refused()
    {
        using var api = new ApiFactory { Settings = { ["Jwt:SigningKey"] = "short" } };

        Assert.Contains("at least 32", Assert.ThrowsAny<Exception>(() => api.CreateClient()).ToString());
    }

    [Fact]
    public async Task Production_with_real_secrets_starts_and_accepts_that_password()
    {
        using var api = new ApiFactory
        {
            Environment = "Production",
            Settings = { ["Jwt:SigningKey"] = StrongKey, ["Auth:Admin:PasswordHash"] = PasswordHashing.Hash("S3cret-for-test") },
        };
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "S3cret-for-test" });
        var devPassword = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = AuthSecrets.DevelopmentAdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, devPassword.StatusCode);
    }
}

public class PasswordToolTests
{
    [Fact]
    public void Hash_password_command_prints_a_hash_the_verifier_accepts()
    {
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.True(HashPasswordCommand.TryRun(["hash-password", "Pa55word!"]));
        }
        finally
        {
            Console.SetOut(original);
        }

        var hash = output.ToString().Trim();
        Assert.True(PasswordHashing.Verify(hash, "Pa55word!"));
        Assert.False(PasswordHashing.Verify(hash, "pa55word!"));
    }

    [Fact]
    public void Hash_password_reads_the_password_from_standard_input_when_not_given()
    {
        var output = new StringWriter();
        var (originalOut, originalIn) = (Console.Out, Console.In);
        Console.SetOut(output);
        Console.SetIn(new StringReader("From-stdin-1" + Environment.NewLine));
        try
        {
            Assert.True(HashPasswordCommand.TryRun(["hash-password"]));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
        }

        Assert.True(PasswordHashing.Verify(output.ToString().Trim(), "From-stdin-1"));
    }

    [Fact]
    public void Normal_start_arguments_are_not_the_tool() =>
        Assert.False(HashPasswordCommand.TryRun(["--urls", "http://localhost:5200"]));

    [Fact]
    public void A_malformed_configured_hash_means_no_login_not_a_crash() =>
        Assert.False(new AdminCredentialsVerifier(new AdminOptions { PasswordHash = "not-a-hash" }).Verify("admin", "x"));
}
