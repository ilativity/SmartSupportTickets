using SupportTickets.Adapters.Files;
using SupportTickets.Core.Domain;

namespace SupportTickets.Api.Endpoints;

/// <summary>
/// Serves uploaded images. Deliberately not static-file middleware over the data folder: that folder also holds
/// tickets.json (every customer's email). Only names the image store itself generates are served.
/// </summary>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/uploads/{fileName}", (string fileName, LocalImageStore store, HttpContext http) =>
        {
            if (store.Find(fileName) is not { } image)
            {
                return Results.NotFound();
            }

            // The type comes from the stored, validated kind; nosniff stops the browser from second-guessing it, and a
            // restrictive CSP makes the file inert even if opened directly.
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'";
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable"; // names are never reused
            return Results.File(image.Path, image.Kind.ContentType());
        }).WithTags("Uploads");
        return app;
    }
}
