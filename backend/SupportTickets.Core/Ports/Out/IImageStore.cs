using SupportTickets.Core.Domain;

namespace SupportTickets.Core.Ports.Out;

/// <summary>
/// Driven port: where uploaded images are kept. Implemented by a local-disk adapter; a scaled-out deployment would
/// use object storage (S3 / Azure Blob) behind the same port.
/// </summary>
public interface IImageStore
{
    /// <summary>
    /// Stores already-validated image bytes under a new, server-chosen name and returns the relative URL
    /// (<c>uploads/{name}.{ext}</c>). The client's file name is never used.
    /// </summary>
    Task<string> SaveAsync(ReadOnlyMemory<byte> content, ImageKind kind, CancellationToken ct);

    /// <summary>
    /// True if <paramref name="url"/> names an image this store actually holds. False for links to files that do not
    /// exist — such as the sample dataset's image names — so such a ticket counts as having no image.
    /// </summary>
    bool Exists(string url);

    /// <summary>Removes an image saved for a ticket that was then not created. Missing files are ignored.</summary>
    Task DeleteAsync(string url, CancellationToken ct);
}
