namespace SupportTickets.Core.Domain;

/// <summary>The image types a ticket may carry. No SVG: it is a document that can contain script.</summary>
public enum ImageKind
{
    Jpeg,
    Png,
    Webp,
}

/// <summary>
/// Decides what an uploaded file really is from its first bytes ("magic bytes"). The file name and the browser's
/// Content-Type are chosen by the client and prove nothing: a script renamed to <c>photo.png</c> is still a script.
/// </summary>
public static class ImageFormat
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    public static ImageKind? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return ImageKind.Jpeg;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ImageKind.Png;
        }

        // RIFF container whose type is WEBP: "RIFF" <4-byte size> "WEBP"
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageKind.Webp;
        }

        return null;
    }

    public static string Extension(this ImageKind kind) => kind switch
    {
        ImageKind.Jpeg => "jpg",
        ImageKind.Png => "png",
        ImageKind.Webp => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ContentType(this ImageKind kind) => kind switch
    {
        ImageKind.Jpeg => "image/jpeg",
        ImageKind.Png => "image/png",
        ImageKind.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
