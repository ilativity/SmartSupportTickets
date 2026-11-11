using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SupportTickets.Tests.Api;

public sealed class TicketEndpointsTests : IDisposable
{
    private const string SeedId = "b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1"; // John Doe, laptop, New

    private readonly ApiFactory _api = new();
    private readonly HttpClient _client;

    public TicketEndpointsTests() => _client = _api.CreateClient();

    public void Dispose() => _api.Dispose();

    private static FormUrlEncodedContent Form(string? name = "Ann Lee", string? email = "ann@example.com",
        string? description = "The printer jams on every page.")
    {
        var fields = new Dictionary<string, string>();
        if (name is not null) fields["name"] = name;
        if (email is not null) fields["email"] = email;
        if (description is not null) fields["description"] = description;
        return new FormUrlEncodedContent(fields);
    }

    private async Task<JsonArray> ListAsync(string query = "")
    {
        var response = await _client.GetAsync("/api/tickets" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
    }

    // ---- list ----

    [Fact]
    public async Task List_returns_the_seed_newest_first_and_never_an_email()
    {
        var list = await ListAsync();

        Assert.Equal(5, list.Count);
        var created = list.Select(t => DateTimeOffset.Parse((string)t!["createdAt"]!)).ToList();
        Assert.Equal(created.OrderByDescending(d => d), created);
        Assert.All(list, t => Assert.Null(t!["email"]));        // D4
        Assert.DoesNotContain("@", list.ToJsonString());         // not under any other name either
    }

    [Fact]
    public async Task List_filters_by_status_with_its_display_name()
    {
        var list = await ListAsync("?status=" + Uri.EscapeDataString("In Progress"));

        Assert.NotEmpty(list);
        Assert.All(list, t => Assert.Equal("In Progress", (string)t!["status"]!));
    }

    [Fact]
    public async Task List_searches_name_and_description()
    {
        Assert.Single(await ListAsync("?search=LAPTOP"));
        Assert.Single(await ListAsync("?search=jane"));
        Assert.Empty(await ListAsync("?search=zzz-nothing"));
    }

    [Theory]
    [InlineData("?status=Done")]
    [InlineData("?status=1")]
    [InlineData("?status=InProgress")]
    public async Task List_rejects_an_unknown_status_with_a_validation_problem(string query)
    {
        var response = await _client.GetAsync("/api/tickets" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(problem["errors"]!["status"]);
    }

    [Fact]
    public async Task List_rejects_an_overlong_search() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tickets?search=" + new string('x', 101))).StatusCode);

    [Fact]
    public async Task Statuses_are_served_in_workflow_order() =>
        Assert.Equal(["New", "In Progress", "Resolved", "Closed"],
            (await _client.GetFromJsonAsync<string[]>("/api/tickets/statuses"))!);

    // ---- details ----

    [Fact]
    public async Task Details_mask_the_email_for_anonymous_callers()
    {
        var ticket = await _client.GetFromJsonAsync<JsonObject>($"/api/tickets/{SeedId}");

        Assert.Equal("j***@example.com", (string)ticket!["email"]!);
        Assert.Equal("/uploads/laptop_issue.jpg", (string)ticket["imageUrl"]!);
    }

    [Fact]
    public async Task Details_of_an_unknown_id_is_404_problem()
    {
        var response = await _client.GetAsync($"/api/tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Details_with_a_non_guid_id_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/tickets/not-a-guid")).StatusCode);

    // ---- create ----

    [Fact]
    public async Task Anonymous_create_returns_201_with_location_and_emails_the_customer()
    {
        var response = await _client.PostAsync("/api/tickets", Form());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var id = (string)ticket["id"]!;
        Assert.Equal($"/api/tickets/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("New", (string)ticket["status"]!);
        Assert.Equal("a***@example.com", (string)ticket["email"]!);
        Assert.Null(ticket["summary"]); // Ai:Provider = None

        var mail = Assert.Single(_api.Mail.Outbox);
        Assert.Equal("ann@example.com", mail.To);
        Assert.Contains($"http://localhost:5201/tickets/{id}", mail.TextBody);

        Assert.Equal(6, (await ListAsync()).Count);
    }

    [Fact]
    public async Task Create_with_missing_fields_returns_every_field_error()
    {
        // One unrelated field, so the request has a form body with none of the required fields in it.
        var response = await _client.PostAsync("/api/tickets",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["other"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        Assert.Equal(["description", "email", "name"], errors.Select(e => e.Key).Order());
        Assert.Empty(_api.Mail.Outbox);
    }

    [Theory]
    [InlineData("  a ", "ann@example.com", "The printer jams on every page.", "name")]              // 1 char after trimming
    [InlineData("Ann", "ann@example.com", "    short     ", "description")]                       // 5 chars after trimming
    [InlineData("Ann", "ann@example", "The printer jams on every page.", "email")]                  // no dot in domain
    [InlineData("Ann", "a@b@c.com", "The printer jams on every page.", "email")]
    [InlineData("Ann", "Ann <ann@example.com>", "The printer jams on every page.", "email")]        // display-name form
    [InlineData("Ann", "ann@example.com\r\nBcc: x@evil.test", "The printer jams on every page.", "email")]
    public async Task Create_rejects_invalid_values(string name, string email, string description, string field)
    {
        var response = await _client.PostAsync("/api/tickets", Form(name, email, description));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        Assert.Equal([field], errors.Select(e => e.Key));
        Assert.Equal(5, (await ListAsync()).Count);
    }

    [Fact]
    public async Task Create_without_any_body_is_400_not_500()
    {
        var response = await _client.PostAsync("/api/tickets", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_accepts_multipart_form_data_as_the_browser_sends_it()
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Ann Lee"), "name" },
            { new StringContent("ann@example.com"), "email" },
            { new StringContent("The printer jams on every page."), "description" },
        };

        var response = await _client.PostAsync("/api/tickets", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_json_instead_of_a_form_is_a_client_error_not_500()
    {
        var response = await _client.PostAsync("/api/tickets",
            new StringContent("{\"name\":\"Ann\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.True((int)response.StatusCode is >= 400 and < 500, $"got {(int)response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_rejects_overlong_values()
    {
        var response = await _client.PostAsync("/api/tickets", Form(name: new string('n', 101), description: new string('d', 2001)));

        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        Assert.Equal(["description", "name"], errors.Select(e => e.Key).Order());
    }

    [Fact]
    public async Task Create_trims_what_it_stores()
    {
        var response = await _client.PostAsync("/api/tickets", Form(name: "  Ann Lee  ", description: "  The printer jams on every page.  "));

        var ticket = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("Ann Lee", (string)ticket["name"]!);
        Assert.Equal("The printer jams on every page.", (string)ticket["description"]!);
    }

    // ---- infrastructure ----

    [Fact]
    public async Task Health_reports_healthy() =>
        Assert.Equal("Healthy", await _client.GetStringAsync("/health"));

    [Fact]
    public async Task OpenApi_document_is_served() =>
        Assert.Contains("/api/tickets", await _client.GetStringAsync("/openapi/v1.json"));

    [Fact]
    public async Task Unknown_api_route_is_a_problem_document()
    {
        var response = await _client.GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Json_property_names_are_camelCase() =>
        Assert.Contains("\"createdAt\"", await _client.GetStringAsync($"/api/tickets/{SeedId}"));
}

public class EmailMaskingTests
{
    [Theory]
    [InlineData("john.doe@example.com", "j***@example.com")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData("no-at-sign", "***")]
    [InlineData("@example.com", "***")]
    [InlineData("𝒜nn@example.com", "𝒜***@example.com")] // first letter outside the BMP stays whole
    public void MaskEmail_keeps_first_letter_and_domain_only(string email, string expected) =>
        Assert.Equal(expected, SupportTickets.Api.Dtos.TicketMapping.MaskEmail(email));
}
