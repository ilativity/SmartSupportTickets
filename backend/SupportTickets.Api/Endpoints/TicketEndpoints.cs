using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SupportTickets.Api.Dtos;
using SupportTickets.Api.Validation;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;

namespace SupportTickets.Api.Endpoints;

/// <summary>
/// The HTTP driving adapter for tickets. Each handler is thin: bind → validate → call the use case → map to a DTO.
/// No business rule lives here.
/// </summary>
public static class TicketEndpoints
{
    private const int MaxSearchLength = 100;

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/tickets").WithTags("Tickets");

        tickets.MapGet("/", ListAsync);
        tickets.MapGet("/statuses", () => TypedResults.Ok(TicketStatusNames.All));
        tickets.MapGet("/{id:guid}", GetAsync).WithName("GetTicket");

        tickets.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateTicketRequest>>()
            // Form binding turns on ASP.NET Core's anti-forgery check by default. That check protects cookie-based
            // sessions from cross-site form posts (CSRF). This API keeps no cookies — admins authenticate with a
            // bearer token the browser never sends on its own — so there is nothing for CSRF to ride on.
            .DisableAntiforgery();

        return app;
    }

    private static async Task<Results<Ok<List<TicketListItemDto>>, ValidationProblem>> ListAsync(
        string? status, string? search, ITicketUseCases useCases, CancellationToken ct)
    {
        TicketStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TicketStatusNames.TryParse(status, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status. Use one of: {string.Join(", ", TicketStatusNames.All)}."],
                });
            }

            statusFilter = parsed;
        }

        if (search?.Length > MaxSearchLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["search"] = [$"Search text must be at most {MaxSearchLength} characters."],
            });
        }

        var found = await useCases.ListAsync(new TicketQuery(statusFilter, search), ct);
        return TypedResults.Ok(found.Select(t => t.ToListItem()).ToList());
    }

    private static async Task<Results<Ok<TicketDetailsDto>, NotFound>> GetAsync(
        Guid id, HttpContext http, ITicketUseCases useCases, CancellationToken ct)
    {
        var ticket = await useCases.GetAsync(id, ct);
        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ticket.ToDetails(showEmail: IsAdmin(http)));
    }

    private static async Task<Created<TicketDetailsDto>> CreateAsync(
        [FromForm] CreateTicketRequest request, ITicketUseCases useCases, CancellationToken ct)
    {
        // The validation filter has already rejected nulls, so the "!" here are safe.
        var ticket = await useCases.CreateAsync(
            new CreateTicketCommand(request.Name!, request.Email!, request.Description!), ct);

        // The person who just created the ticket typed the address themselves, but the response still follows D4:
        // the same JSON is what any client would see when fetching this ticket.
        return TypedResults.Created($"/api/tickets/{ticket.Id}", ticket.ToDetails(showEmail: false));
    }

    // Replaced by the real role check when authentication is added (next commit); until then nobody is an admin.
    private static bool IsAdmin(HttpContext http) => false;
}
