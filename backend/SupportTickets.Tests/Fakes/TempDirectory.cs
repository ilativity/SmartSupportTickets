namespace SupportTickets.Tests.Fakes;

/// <summary>A fresh folder per test, deleted afterwards, so tests never share or leave data files.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sst-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still open by a failing test must not hide that test's real error.
        }
    }
}

/// <summary>Paths inside the repository, found by walking up to the solution file.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    /// <summary>The provided seed data.</summary>
    public static string Dataset => System.IO.Path.Combine(Root, "dataset.json");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "MX_EX.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (MX_EX.slnx) not found");
    }
}
