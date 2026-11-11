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
        var ai = config.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
        switch (ai.Provider.ToLowerInvariant())
        {
            case "none":
                services.AddSingleton<ISummaryGenerator, NoSummaryGenerator>();
                break;
            case "gemini":
                ai.Gemini.ApiKey = RequireKey(ai.Gemini.ApiKey, "Ai:Gemini:ApiKey");
                services.AddSingleton(ai.Gemini);
                services.AddHttpClient<ISummaryGenerator, GeminiSummaryGenerator>(http => ConfigureAiClient(http, ai.Gemini.BaseUrl));
                break;
            case "openai":
                ai.OpenAI.ApiKey = RequireKey(ai.OpenAI.ApiKey, "Ai:OpenAI:ApiKey");
                services.AddSingleton(ai.OpenAI);
                services.AddHttpClient<ISummaryGenerator, OpenAiSummaryGenerator>(http => ConfigureAiClient(http, ai.OpenAI.BaseUrl));
                break;
            default:
                throw new InvalidOperationException($"Unknown Ai:Provider '{ai.Provider}'. Use None, Gemini or OpenAI.");
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

    /// <summary>
    /// Choosing a provider without its key is a configuration mistake: say so at startup, not per ticket. The key is
    /// trimmed: a trailing newline from a secrets file would make every request header invalid.
    /// </summary>
    private static string RequireKey(string? key, string setting) =>
        string.IsNullOrWhiteSpace(key)
            ? throw new InvalidOperationException($"{setting} is required when that AI provider is selected.")
            : key.Trim();

    private static void ConfigureAiClient(HttpClient http, string baseUrl)
    {
        // Trailing slash so a base URL with a path (e.g. a proxy at https://host/gemini) keeps that path when the
        // adapter appends its relative route.
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        // A backstop only: the use case already gives the whole summary step a 10-second budget.
        http.Timeout = TimeSpan.FromSeconds(15);
    }

    private sealed class GuidIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
