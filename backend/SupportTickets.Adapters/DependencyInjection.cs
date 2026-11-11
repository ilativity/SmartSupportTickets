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
            case "smtp":
                services.AddSingleton(ValidateSmtp(config.GetSection("Email:Smtp").Get<SmtpOptions>() ?? new SmtpOptions()));
                services.AddSingleton<INotificationSender, SmtpNotificationSender>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Email:Provider '{emailProvider}'. Use Console or Smtp.");
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

    /// <summary>A half-configured mail server is a startup error, not a failure on every ticket.</summary>
    private static SmtpOptions ValidateSmtp(SmtpOptions smtp)
    {
        _ = SmtpNotificationSender.SecurityOption(smtp.Security); // throws on an unknown value
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            throw new InvalidOperationException("Email:Smtp:Host and Email:Smtp:FromAddress are required when Email:Provider is Smtp.");
        }

        if (!MimeKit.MailboxAddress.TryParse(smtp.FromAddress, out _))
        {
            throw new InvalidOperationException($"Email:Smtp:FromAddress '{smtp.FromAddress}' is not a valid email address.");
        }

        if (smtp.Port is < 1 or > 65535 || smtp.TimeoutSeconds < 1)
        {
            throw new InvalidOperationException("Email:Smtp:Port must be 1-65535 and Email:Smtp:TimeoutSeconds at least 1.");
        }

        if (!string.IsNullOrEmpty(smtp.Username) && string.IsNullOrWhiteSpace(smtp.Password))
        {
            throw new InvalidOperationException("Email:Smtp:Password is required when Email:Smtp:Username is set.");
        }

        // A password over an unencrypted connection could be read by anyone on the network path.
        if (!string.IsNullOrEmpty(smtp.Username) && smtp.Security.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Email:Smtp:Security=None cannot be combined with a username and password.");
        }

        return smtp;
    }

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
