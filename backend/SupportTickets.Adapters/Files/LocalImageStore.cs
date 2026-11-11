using System.Text.RegularExpressions;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Files;

/// <param name="Directory">Absolute folder for uploads (inside the git-ignored App_Data).</param>
public sealed record ImageStoreOptions(string Directory);

/// <summary>
/// Images on the local disk. Every file gets a new GUID name chosen here, so a client can never pick a path
/// ("../../appsettings.json"), overwrite another file, or make the server store a name that later means something.
/// Single-instance by nature, like the JSON repository (see README, "Scaling").
/// </summary>
public sealed partial class LocalImageStore(ImageStoreOptions options) : IImageStore
{
    /// <summary>The relative URL prefix the API serves these files under.</summary>
    public const string UrlPrefix = "uploads/";

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> content, ImageKind kind, CancellationToken ct)
    {
        Directory.CreateDirectory(options.Directory);
        var name = $"{Guid.NewGuid():N}.{kind.Extension()}";

        var path = Path.Combine(options.Directory, name);
        try
        {
            // CreateNew: never overwrite, even in the astronomically unlikely case of a repeated GUID.
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await file.WriteAsync(content, ct);
        }
        catch
        {
            File.Delete(path); // no half-written files left behind (Delete ignores a missing file)
            throw;
        }

        return UrlPrefix + name;
    }

    public Task DeleteAsync(string url, CancellationToken ct)
    {
        // Only names this store makes; anything else is ignored rather than turned into a path.
        if (url.StartsWith(UrlPrefix, StringComparison.Ordinal) && Find(url[UrlPrefix.Length..]) is { } image)
        {
            File.Delete(image.Path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a requested file name to a stored image, or null. Only names this store could have produced are
    /// accepted (32 hex digits + a known extension), so the download endpoint cannot be used to read any other file.
    /// </summary>
    public (string Path, ImageKind Kind)? Find(string fileName)
    {
        var match = StoredName().Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        var kind = match.Groups["ext"].Value switch
        {
            "jpg" => ImageKind.Jpeg,
            "png" => ImageKind.Png,
            _ => ImageKind.Webp,
        };
        var path = Path.Combine(options.Directory, fileName);
        return File.Exists(path) ? (path, kind) : null;
    }

    // \z rather than $: in .NET "$" also matches just before a final line break, so "<hex>.png" + "\n" would pass.
    [GeneratedRegex("^[0-9a-f]{32}\\.(?<ext>jpg|png|webp)\\z")]
    private static partial Regex StoredName();
}
