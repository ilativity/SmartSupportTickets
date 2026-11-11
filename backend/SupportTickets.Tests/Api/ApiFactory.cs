using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportTickets.Core.Ports.Out;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Api;

/// <summary>
/// The real API in memory, with its own temporary data folder seeded from dataset.json, and the outgoing ports
/// (email) replaced by fakes so tests can read what would have been sent. Everything else is the production wiring.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TempDirectory _data = new();

    public FakeNotificationSender Mail { get; } = new();

    /// <summary>The temporary data folder this API instance uses.</summary>
    public string DataPath => _data.Path;

    public string Environment { get; init; } = "Development";

    public Dictionary<string, string?> Settings { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting("Storage:DataDirectory", _data.Path);
        builder.UseSetting("Storage:SeedFile", RepoPaths.Dataset);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationSender>();
            services.AddSingleton<INotificationSender>(Mail);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _data.Dispose();
    }
}
