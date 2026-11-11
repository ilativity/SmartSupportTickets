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

    /// <summary>Authorization policy for editing: a valid admin JWT (D3).</summary>
    public const string AdminOnlyPolicy = "AdminOnly";

    /// <summary>Rate-limiter policy for anonymous ticket creation (configured in Program).</summary>
    public const string CreateRateLimitPolicy = "create-ticket";

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/tickets").WithTags("Tickets");

        tickets.MapGet("/", ListAsync);
        tickets.MapGet("/statuses", () => TypedResults.Ok(TicketStatusNames.All));
        tickets.MapGet("/{id:guid}", GetAsync).WithName("GetTicket");

        tickets.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateTicketRequest>>()
            .RequireRateLimiting(CreateRateLimitPolicy)
            // 5 MB image + the text fields. A larger body is cut off by the server with 413 before it is read.
            .WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024))
            // Form binding turns on ASP.NET Core's anti-forgery check by default. That check protects cookie-based
            // sessions from cross-site form posts (CSRF). This API keeps no cookies — admins authenticate with a
            // bearer token the browser never sends on its own — so there is nothing for CSRF to ride on.
            .DisableAntiforgery();

        tickets.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(AdminOnlyPolicy)
            .AddEndpointFilter<ValidationFilter<UpdateTicketRequest>>();

        tickets.MapPost("/{id:guid}/image", AttachImageAsync)
            .RequireAuthorization(AdminOnlyPolicy)
            .AddEndpointFilter<ValidationFilter<AttachImageRequest>>()
            .WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024))
            .DisableAntiforgery(); // bearer token, no cookies: see the note on POST /api/tickets

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
        // The validation filter has already rejected nulls and non-images, so the "!" here are safe.
        var ticket = await useCases.CreateAsync(
            new CreateTicketCommand(request.Name!, request.Email!, request.Description!, await ReadImageAsync(request.Image, ct)), ct);

        // The person who just created the ticket typed the address themselves, but the response still follows D4:
        // the same JSON is what any client would see when fetching this ticket.
        return TypedResults.Created($"/api/tickets/{ticket.Id}", ticket.ToDetails(showEmail: false));
    }

    private static async Task<ImageUpload?> ReadImageAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            return null;
        }

        // At most 5 MB (validated), so reading it into memory is fine and lets Core stay free of ASP.NET types.
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        return new ImageUpload(bytes, ImageFormat.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, ImageFormat.HeaderLength)))!.Value);
    }

    private static async Task<Results<Ok<TicketDetailsDto>, NotFound, ProblemHttpResult>> AttachImageAsync(
        Guid id, [FromForm] AttachImageRequest request, ITicketUseCases useCases, CancellationToken ct)
    {
        var result = await useCases.AttachImageAsync(id, (await ReadImageAsync(request.Image, ct))!, ct);
        return result.Outcome switch
        {
            AttachImageOutcome.Attached => TypedResults.Ok(result.Ticket!.ToDetails(showEmail: true)),
            AttachImageOutcome.NotFound => TypedResults.NotFound(),
            // Replacing what the customer sent is not offered: the image is part of their request.
            _ => TypedResults.Problem("This ticket already has an image.", statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<Ok<TicketDetailsDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, UpdateTicketRequest request, ITicketUseCases useCases, CancellationToken ct)
    {
        if (!TicketStatusNames.TryParse(request.Status, out var status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Unknown status. Use one of: {string.Join(", ", TicketStatusNames.All)}."],
            });
        }

        var ticket = await useCases.UpdateAsync(new UpdateTicketCommand(id, status, request.Resolution), ct);
        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ticket.ToDetails(showEmail: true)); // only admins reach this endpoint
    }

    // An anonymous request, or one with an invalid/expired token, is simply not an admin: it still gets the ticket,
    // with the email masked.
    private static bool IsAdmin(HttpContext http) => http.User.IsInRole(SupportTickets.Api.Auth.TokenService.AdminRole);
}
