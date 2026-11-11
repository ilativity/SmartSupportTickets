// Composition root and HTTP driving adapter. Wiring is filled in commit by commit (see docs/SPEC.md §9).
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so WebApplicationFactory<Program> in the test project can host the API in memory.
public partial class Program;
