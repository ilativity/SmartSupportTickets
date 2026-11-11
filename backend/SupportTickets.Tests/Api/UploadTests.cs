using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Api;

public sealed class UploadTests : IDisposable
{
    private readonly ApiFactory _api = new();
    private readonly HttpClient _client;

    public UploadTests() => _client = _api.CreateClient();

    public void Dispose() => _api.Dispose();

    private Task<HttpResponseMessage> CreateWithImage(byte[] bytes, string fileName = "photo.png", string contentType = "image/png")
    {
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent
        {
            { new StringContent("Ann Lee"), "name" },
            { new StringContent("ann@example.com"), "email" },
            { new StringContent("The printer jams on every page."), "description" },
            { image, "image", fileName },
        };
        return _client.PostAsync("/api/tickets", form);
    }

    [Fact]
    public async Task A_real_png_is_stored_and_served_back_with_safe_headers()
    {
        var created = await CreateWithImage(TestImages.Png, fileName: "../../evil name.png");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var imageUrl = (string)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["imageUrl"]!;
        Assert.Matches("^/uploads/[0-9a-f]{32}\\.png$", imageUrl); // the client's file name is not used

        var image = await _client.GetAsync(imageUrl);

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(TestImages.Png, await image.Content.ReadAsByteArrayAsync());

        var list = await _client.GetStringAsync("/api/tickets?search=Ann%20Lee");
        Assert.Contains("\"hasImage\":true", list);
    }

    [Fact]
    public async Task Text_named_png_with_an_image_content_type_is_rejected()
    {
        var response = await CreateWithImage("not an image at all"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        Assert.Equal(["image"], errors.Select(e => e.Key));
        Assert.Contains("JPEG, PNG or WebP", (string)errors["image"]![0]!);
    }

    [Fact]
    public async Task An_image_over_5_MB_is_rejected()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        TestImages.Png.CopyTo(big, 0);

        var response = await CreateWithImage(big);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("5 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rejected_image_creates_no_ticket_and_stores_no_file()
    {
        var before = JsonNode.Parse(await _client.GetStringAsync("/api/tickets"))!.AsArray().Count;

        var response = await CreateWithImage("<svg onload=alert(1)>"u8.ToArray(), "x.svg", "image/svg+xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, JsonNode.Parse(await _client.GetStringAsync("/api/tickets"))!.AsArray().Count);
        Assert.Empty(_api.Mail.Outbox);
        var uploads = Path.Combine(_api.DataPath, "uploads");
        Assert.True(!Directory.Exists(uploads) || Directory.GetFiles(uploads).Length == 0);
    }

    [Fact]
    public async Task Creating_tickets_is_rate_limited_per_client()
    {
        using var api = new ApiFactory { Settings = { ["Tickets:CreateRateLimit:PermitLimit"] = "2" } };
        var client = api.CreateClient();
        Task<HttpResponseMessage> Post() => client.PostAsync("/api/tickets", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Ann Lee", ["email"] = "ann@example.com", ["description"] = "The printer jams on every page.",
        }));

        Assert.Equal(HttpStatusCode.Created, (await Post()).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post()).StatusCode);
        var third = await Post();
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/uploads/tickets.json")]
    [InlineData("/uploads/..%2Ftickets.json")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.png")]
    public async Task Nothing_but_stored_images_can_be_downloaded(string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path)).StatusCode);
}
