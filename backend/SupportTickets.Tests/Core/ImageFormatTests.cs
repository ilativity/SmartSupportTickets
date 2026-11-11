using SupportTickets.Core.Domain;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Core;

public class ImageFormatTests
{
    [Fact]
    public void Recognises_the_three_accepted_formats_by_their_bytes()
    {
        Assert.Equal(ImageKind.Png, ImageFormat.Detect(TestImages.Png));
        Assert.Equal(ImageKind.Jpeg, ImageFormat.Detect(TestImages.Jpeg));
        Assert.Equal(ImageKind.Webp, ImageFormat.Detect(TestImages.Webp));
    }

    public static TheoryData<string, byte[]> NotImages => new()
    {
        { "text", "hello, I am a .png"u8.ToArray() },
        { "html with script", "<html><script>alert(1)</script>"u8.ToArray() },
        { "svg (can contain script)", "<svg xmlns='http://www.w3.org/2000/svg'>"u8.ToArray() },
        { "gif (not accepted)", "GIF89a\0\0\0\0\0\0"u8.ToArray() },
        { "RIFF but not WebP (a WAV file)", [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVEfmt "u8] },
        { "truncated PNG signature", [0x89, 0x50, 0x4E] },
        { "empty", [] },
    };

    [Theory]
    [MemberData(nameof(NotImages))]
    public void Rejects_anything_else(string what, byte[] bytes) =>
        Assert.True(ImageFormat.Detect(bytes) is null, what);
}
