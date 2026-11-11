using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SupportTickets.Tests.Api;

// Proves the test host can start the real Program; replaced by proper integration tests in commit 4.
public class SmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
