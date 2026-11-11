using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SupportTickets.Adapters.Persistence;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Adapters;

public sealed class JsonTicketRepositoryTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private string DataFile => _dir.File("tickets.json");

    private JsonTicketRepository Repository(bool seeded = true) =>
        new(new JsonStorageOptions(_dir.Path, seeded ? RepoPaths.Dataset : null), NullLogger<JsonTicketRepository>.Instance);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task First_use_seeds_from_the_dataset_without_changing_it()
    {
        var datasetBytes = await File.ReadAllBytesAsync(RepoPaths.Dataset);
        using var repository = Repository();

        var all = await repository.ListAsync(new TicketQuery(), default);
        await repository.AddAsync(TestTickets.Make(), default);

        Assert.Equal(JsonNode.Parse(datasetBytes)!.AsArray().Count, all.Count);
        Assert.Contains(all, t => t.Status == TicketStatus.InProgress); // "In Progress" with a space was read
        Assert.Equal(datasetBytes, await File.ReadAllBytesAsync(RepoPaths.Dataset));
        Assert.True(File.Exists(DataFile));
    }

    [Fact]
    public async Task Saving_keeps_every_seed_field_spelling_and_date_format()
    {
        using (var repository = Repository())
        {
            await repository.AddAsync(TestTickets.Make(), default); // forces a write of the whole file
        }

        var original = JsonNode.Parse(await File.ReadAllTextAsync(RepoPaths.Dataset))!.AsArray();
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(DataFile))!.AsArray();

        for (var i = 0; i < original.Count; i++)
        {
            Assert.True(JsonNode.DeepEquals(original[i], saved[i]), $"ticket {i} changed:\n{original[i]}\n---\n{saved[i]}");
        }
    }

    [Fact]
    public async Task Data_survives_a_restart()
    {
        var ticket = TestTickets.Make(name: "Persisted");
        using (var first = Repository())
        {
            await first.AddAsync(ticket, default);
        }

        using var second = Repository();
        Assert.Equal(ticket, await second.GetAsync(ticket.Id, default));
    }

    [Fact]
    public async Task Twenty_parallel_adds_all_end_up_in_a_valid_file()
    {
        using (var repository = Repository(seeded: false))
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Task.Run(() => repository.AddAsync(TestTickets.Make(name: $"T{i}"), default))));
        }

        using var reopened = Repository(seeded: false);
        var names = (await reopened.ListAsync(new TicketQuery(), default)).Select(t => t.Name).ToHashSet();
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"T{i}").ToHashSet(), names);
    }

    [Fact]
    public async Task Concurrent_updates_are_both_applied_and_each_sees_the_other_as_before()
    {
        using var repository = Repository(seeded: false);
        var ticket = TestTickets.Make();
        await repository.AddAsync(ticket, default);

        // Two admins save at the same moment: one changes the status, the other the resolution.
        var updates = await Task.WhenAll(
            Task.Run(() => repository.UpdateAsync(ticket.Id, t => t with { Status = TicketStatus.Closed }, default)),
            Task.Run(() => repository.UpdateAsync(ticket.Id, t => t with { Resolution = "Fixed" }, default)));

        var final = await repository.GetAsync(ticket.Id, default);
        Assert.Equal(TicketStatus.Closed, final!.Status);
        Assert.Equal("Fixed", final.Resolution);

        // Serialised, not interleaved: exactly one of them started from the original ticket, the other from the
        // first one's result — so each email describes exactly its own change.
        Assert.Single(updates, u => u!.Before == ticket);
        Assert.Single(updates, u => u!.Before != ticket && u.Before == updates.Single(x => x!.Before == ticket)!.After);
    }

    [Fact]
    public async Task Update_without_change_does_not_write_the_file()
    {
        using var repository = Repository();
        var first = (await repository.ListAsync(new TicketQuery(), default))[0];
        // A marker the serializer never writes: if the file is rewritten, the marker disappears.
        await File.AppendAllTextAsync(DataFile, "\n\n");

        var update = await repository.UpdateAsync(first.Id, t => t, default);

        Assert.Same(update!.Before, update.After);
        Assert.EndsWith("\n\n", await File.ReadAllTextAsync(DataFile));
    }

    [Fact]
    public async Task Without_a_seed_no_empty_file_is_written_until_the_first_ticket()
    {
        using var repository = Repository(seeded: false);

        Assert.Empty(await repository.ListAsync(new TicketQuery(), default));
        Assert.False(File.Exists(DataFile));

        await repository.AddAsync(TestTickets.Make(), default);
        Assert.True(File.Exists(DataFile));
    }

    [Fact]
    public async Task A_configured_seed_that_does_not_exist_is_an_error_not_an_empty_store()
    {
        using var repository = new JsonTicketRepository(
            new JsonStorageOptions(_dir.Path, _dir.File("typo-dataset.json")), NullLogger<JsonTicketRepository>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(() => repository.ListAsync(new TicketQuery(), default));
        Assert.False(File.Exists(DataFile));
    }

    [Theory]
    [InlineData("""[{"id":"b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1","name":null,"email":"a@b.c","description":"d","status":"New","resolution":"","createdAt":"2025-01-01T00:00:00Z","updatedAt":"2025-01-01T00:00:00Z"}]""")]
    [InlineData("""[{"id":"b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1","name":"A","email":"a@b.c","description":"d","status":"New","resolution":"","createdAt":null,"updatedAt":"2025-01-01T00:00:00Z"}]""")]
    public async Task A_record_with_a_missing_value_is_reported_with_the_file_name(string json)
    {
        await File.WriteAllTextAsync(DataFile, json);
        using var repository = Repository();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => repository.ListAsync(new TicketQuery(), default));
        Assert.Contains("tickets.json", ex.Message);
    }

    [Fact]
    public async Task Update_of_unknown_id_returns_null()
    {
        using var repository = Repository();
        Assert.Null(await repository.UpdateAsync(Guid.NewGuid(), t => t, default));
    }

    [Fact]
    public async Task A_corrupt_file_is_reported_and_left_untouched()
    {
        await File.WriteAllTextAsync(DataFile, "[ { this is not json");
        using var repository = Repository();

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.AddAsync(TestTickets.Make(), default));

        Assert.Equal("[ { this is not json", await File.ReadAllTextAsync(DataFile));
    }

    [Fact]
    public async Task An_unknown_status_in_the_file_is_reported_not_guessed()
    {
        await File.WriteAllTextAsync(DataFile, """
            [{"id":"b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1","name":"A","email":"a@b.c","description":"d",
              "status":"Done","resolution":"","createdAt":"2025-01-01T00:00:00Z","updatedAt":"2025-01-01T00:00:00Z"}]
            """);

        using var repository = Repository();
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ListAsync(new TicketQuery(), default));
    }

    [Fact]
    public async Task A_failed_write_leaves_memory_unchanged()
    {
        using var repository = Repository();
        var before = await repository.ListAsync(new TicketQuery(), default);

        // A directory where the temporary file must go makes the write fail on every OS.
        Directory.CreateDirectory(DataFile + ".tmp");
        // (Windows reports UnauthorizedAccessException, Linux IOException — either is a failed write.)
        await Assert.ThrowsAnyAsync<Exception>(() => repository.AddAsync(TestTickets.Make(name: "lost"), default));

        Assert.Equal(before, await repository.ListAsync(new TicketQuery(), default));
        Directory.Delete(DataFile + ".tmp");
        using var reopened = Repository();
        Assert.DoesNotContain(await reopened.ListAsync(new TicketQuery(), default), t => t.Name == "lost");
    }
}

public class AdapterWiringTests
{
    // A typo in configuration must stop the app at startup, not silently run without AI or email.
    [Theory]
    [InlineData("Ai:Provider", "Gemni")]
    [InlineData("Email:Provider", "Smpt")]
    public void Unknown_provider_names_fail_at_startup(string key, string value)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(void () =>
            SupportTickets.Adapters.DependencyInjection.AddAdapters(
                new Microsoft.Extensions.DependencyInjection.ServiceCollection(), config, Path.GetTempPath()));
        Assert.Contains(value, ex.Message);
    }
}
