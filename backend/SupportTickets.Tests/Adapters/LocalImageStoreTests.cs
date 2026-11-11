using SupportTickets.Adapters.Files;
using SupportTickets.Core.Domain;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Adapters;

public sealed class LocalImageStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private LocalImageStore Store => new(new ImageStoreOptions(Path.Combine(_dir.Path, "uploads")));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Saves_under_a_new_guid_name_and_finds_it_again()
    {
        var url = await Store.SaveAsync(TestImages.Png, ImageKind.Png, default);

        Assert.Matches("^uploads/[0-9a-f]{32}\\.png$", url);
        var found = Store.Find(url["uploads/".Length..]);
        Assert.NotNull(found);
        Assert.Equal(ImageKind.Png, found.Value.Kind);
        Assert.Equal(TestImages.Png, await File.ReadAllBytesAsync(found.Value.Path));
    }

    [Fact]
    public async Task Two_saves_never_share_a_name()
    {
        var first = await Store.SaveAsync(TestImages.Png, ImageKind.Png, default);
        var second = await Store.SaveAsync(TestImages.Png, ImageKind.Png, default);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("../tickets.json")]
    [InlineData("..\\tickets.json")]
    [InlineData("tickets.json")]
    [InlineData("0123456789abcdef0123456789abcdef.svg")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png.exe")]
    [InlineData("")]
    public void Find_refuses_any_name_the_store_could_not_have_made(string name)
    {
        // Something worth stealing, next door and inside the uploads folder itself.
        File.WriteAllText(_dir.File("tickets.json"), "[]");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "uploads"));
        File.WriteAllText(Path.Combine(_dir.Path, "uploads", "tickets.json"), "[]");
        Assert.Null(Store.Find(name));
    }

    [Fact]
    public async Task Delete_removes_a_stored_image_and_ignores_anything_else()
    {
        var url = await Store.SaveAsync(TestImages.Png, ImageKind.Png, default);
        File.WriteAllText(_dir.File("tickets.json"), "[]");

        await Store.DeleteAsync("../tickets.json", default);
        await Store.DeleteAsync("uploads/../tickets.json", default);
        await Store.DeleteAsync(url, default);

        Assert.Null(Store.Find(url["uploads/".Length..]));
        Assert.True(File.Exists(_dir.File("tickets.json")));
    }

    [Fact]
    public void Find_of_a_well_formed_but_missing_name_is_null() =>
        Assert.Null(Store.Find("0123456789abcdef0123456789abcdef.png"));
}
