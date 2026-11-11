// Composition root (wires ports to adapters) and the HTTP driving adapter. Wiring only — no business logic here.
using Scalar.AspNetCore;
using SupportTickets.Adapters;
using SupportTickets.Api.Endpoints;
using SupportTickets.Core.Application;
using SupportTickets.Core.Ports.In;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var frontendBaseUrl = new Uri(config["App:FrontendBaseUrl"]
    ?? throw new InvalidOperationException("App:FrontendBaseUrl is not configured."));

// --- Core (the hexagon) and its driven adapters ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new NotificationComposer(frontendBaseUrl));
builder.Services.AddScoped<ITicketUseCases, TicketUseCases>();
builder.Services.AddAdapters(config, builder.Environment.ContentRootPath);

// --- HTTP concerns ---
// Every error, including unhandled exceptions and bare 404s, is an RFC 7807 ProblemDetails JSON body,
// so the frontend has one error shape to handle.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    // Only the frontend's own origin; in development and Docker the frontend proxies /api, so CORS is a fallback
    // for running the two on different hosts.
    .WithOrigins(frontendBaseUrl.GetLeftPart(UriPartial.Authority))
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// CORS first, so that even error responses (500, 413) carry CORS headers and a cross-origin frontend can read them.
app.UseCors();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A malformed request (no form body, body too large) is the client's error and carries its own status code
    // (400, 413); without this it would be reported as a 500, telling the client it was the server's fault.
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(); // interactive API docs at /scalar/v1
}

app.MapHealthChecks("/health");
app.MapTicketEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> in the test project can host the API in memory.
public partial class Program;
