using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SupportTickets.Api.Auth;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Api;

public sealed class AttachImageApiTests : IDisposable
{
    private const string SeedId = "b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1"; // John Doe: imageUrl points to a missing file

    private readonly ApiFactory _api = new();
    private readonly HttpClient _client;

    public AttachImageApiTests() => _client = _api.CreateClient();

    public void Dispose() => _api.Dispose();

    private static MultipartFormDataContent Image(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "image", "photo.png" } };
    }

    private async Task<HttpResponseMessage> AttachAsync(byte[] bytes, string id = SeedId, bool admin = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{id}/image") { Content = Image(bytes) };
        if (admin)
        {
            var login = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = AuthSecrets.DevelopmentAdminPassword });
            var token = (string)JsonNode.Parse(await login.Content.ReadAsStringAsync())!["token"]!;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_attach_an_image() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await AttachAsync(TestImages.Png, admin: false)).StatusCode);

    [Fact]
    public async Task An_invalid_token_cannot_attach_an_image()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{SeedId}/image") { Content = Image(TestImages.Png) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Admin_attaches_an_image_to_a_ticket_with_a_broken_one_and_it_is_served()
    {
        var response = await AttachAsync(TestImages.Png);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imageUrl = (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["imageUrl"]!;
        Assert.Matches(@"^/uploads/[0-9a-f]{32}\.png$", imageUrl);
        Assert.Equal(TestImages.Png, await _client.GetByteArrayAsync(imageUrl));
        Assert.Empty(_api.Mail.Outbox); // not a status/resolution change: no email
    }

    [Fact]
    public async Task A_second_image_is_refused_with_409()
    {
        Assert.Equal(HttpStatusCode.OK, (await AttachAsync(TestImages.Png)).StatusCode);

        var second = await AttachAsync(TestImages.Jpeg);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Something_that_is_not_an_image_is_a_field_error()
    {
        var response = await AttachAsync("<script>alert(1)</script>"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"image\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_ticket_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await AttachAsync(TestImages.Png, Guid.NewGuid().ToString())).StatusCode);
}
