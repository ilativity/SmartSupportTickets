using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Tests.Fakes;

/// <summary>Keeps uploaded images in memory; can be told to fail like a full disk.</summary>
public sealed class FakeImageStore : IImageStore
{
    public List<(byte[] Content, ImageKind Kind)> Saved { get; } = [];

    public bool Fail { get; set; }

    public List<string> Deleted { get; } = [];

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
        return Task.FromResult($"uploads/fake{Saved.Count}.png");
    }
}

/// <summary>Real image headers: the smallest bytes each format can be recognised from.</summary>
public static class TestImages
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    public static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];
}
