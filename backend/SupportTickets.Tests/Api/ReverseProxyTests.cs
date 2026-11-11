using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SupportTickets.Tests.Api;

/// <summary>
/// Behind nginx every request comes from the proxy. The per-IP login limit must then use X-Forwarded-For — but only
/// when the request really comes from the trusted proxy network; from anywhere else the header is a forgery.
/// </summary>
public class ReverseProxyTests
{
    /// <summary>Makes every in-memory request look as if it arrived from <paramref name="remote"/>.</summary>
    private static HttpClient Client(string remote)
    {
        var api = new ApiFactory
        {
            Settings =
            {
                ["ReverseProxy:TrustedNetwork"] = "172.28.0.0/16",
                ["Auth:LoginRateLimit:PermitLimit"] = "1",
            },
        };
        return api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IStartupFilter>(new RemoteAddress(IPAddress.Parse(remote))))).CreateClient();
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "admin", password = "wrong" }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Through_the_trusted_proxy_each_client_has_its_own_limit()
    {
        var client = Client("172.28.0.3"); // nginx inside the compose network

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "203.0.113.2")).StatusCode); // another client
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, "203.0.113.1")).StatusCode); // first again
    }

    [Fact]
    public async Task From_an_untrusted_address_a_forged_header_does_not_buy_new_attempts()
    {
        var client = Client("198.51.100.7"); // straight from the internet

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, "203.0.113.2")).StatusCode);
    }

    private sealed class RemoteAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
