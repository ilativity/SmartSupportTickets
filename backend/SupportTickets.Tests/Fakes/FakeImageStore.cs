using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Tests.Fakes;

/// <summary>Keeps uploaded images in memory; can be told to fail like a full disk.</summary>
public sealed class FakeImageStore : IImageStore
{
    public List<(byte[] Content, ImageKind Kind)> Saved { get; } = [];

    public bool Fail { get; set; }

    /// <summary>Runs right after a save — lets a test make "someone else" act at exactly that moment.</summary>
    public Func<Task>? AfterSave { get; set; }

    public List<string> Deleted { get; } = [];

    /// <summary>Images that exist besides the ones saved through this fake (e.g. "already on disk").</summary>
    public HashSet<string> Existing { get; } = [];

    public bool Exists(string url) =>
        !Deleted.Contains(url) && (Existing.Contains(url) || Enumerable.Range(1, Saved.Count).Any(i => url == $"uploads/fake{i}.png"));

    public Task DeleteAsync(string url, CancellationToken ct)
    {
        Deleted.Add(url);
        return Task.CompletedTask;
    }

    public Task<string> SaveAsync(ReadOnlyMemory<byte> content, ImageKind kind, CancellationToken ct)
    {
        if (Fail)
        {
            throw new IOException("disk full");
        }

        Saved.Add((content.ToArray(), kind));
        var url = $"uploads/fake{Saved.Count}.png";
        return AfterSave is null ? Task.FromResult(url) : AfterSave().ContinueWith(_ => url);
    }
}

/// <summary>Real image headers: the smallest bytes each format can be recognised from.</summary>
public static class TestImages
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    public static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];
}
