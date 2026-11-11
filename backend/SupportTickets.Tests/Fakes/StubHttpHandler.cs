using System.Net;

namespace SupportTickets.Tests.Fakes;

/// <summary>
/// Answers every HTTP request with a fixed status and body and remembers the last request, so AI adapters can be
/// tested without the network, a key or any cost.
/// </summary>
public sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public HttpClient Client(string baseUrl) => new(this) { BaseAddress = new Uri(baseUrl) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
