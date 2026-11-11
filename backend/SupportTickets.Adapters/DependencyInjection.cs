using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportTickets.Adapters.Ai;
using SupportTickets.Adapters.Notifications;
using SupportTickets.Adapters.Persistence;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters;

/// <summary>
/// Plugs the driven adapters into the ports. The only place that decides "which technology": changing storage, AI
/// provider or email transport is a change here (and in configuration), never in Core.
/// </summary>
public static class DependencyInjection
{
    /// <param name="contentRoot">Base for relative paths in configuration (the API project folder).</param>
    public static IServiceCollection AddAdapters(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        // Persistence — one instance: it owns the lock and the cache.
        services.AddSingleton(new JsonStorageOptions(
            DataDirectory: Path.GetFullPath(config["Storage:DataDirectory"] ?? "App_Data", contentRoot),
            SeedFile: config["Storage:SeedFile"] is { Length: > 0 } seed ? Path.GetFullPath(seed, contentRoot) : null));
        services.AddSingleton<JsonTicketRepository>();
        services.AddSingleton<ITicketRepository>(sp => sp.GetRequiredService<JsonTicketRepository>());

        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        // AI summary (D7). An unknown name stops the app at startup rather than silently running without AI.
        var aiProvider = config["Ai:Provider"] ?? "None";
        switch (aiProvider.ToLowerInvariant())
        {
            case "none":
                services.AddSingleton<ISummaryGenerator, NoSummaryGenerator>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Ai:Provider '{aiProvider}'. Use None.");
        }

        // Email (D8).
        var emailProvider = config["Email:Provider"] ?? "Console";
        switch (emailProvider.ToLowerInvariant())
        {
            case "console":
                services.AddSingleton<INotificationSender, ConsoleNotificationSender>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Email:Provider '{emailProvider}'. Use Console.");
        }

        return services;
    }

    private sealed class GuidIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
