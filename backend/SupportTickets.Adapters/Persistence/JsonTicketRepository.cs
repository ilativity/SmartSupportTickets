using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Persistence;

/// <summary>Where the JSON repository keeps its file and where it seeds it from.</summary>
/// <param name="DataDirectory">Absolute folder for <c>tickets.json</c> (git-ignored <c>App_Data</c>, D6).</param>
/// <param name="SeedFile">Absolute path of the provided <c>dataset.json</c>; copied once when there is no data yet.
/// Never written to, so running the app never changes the committed dataset.</param>
public sealed record JsonStorageOptions(string DataDirectory, string? SeedFile);

/// <summary>
/// Tickets in one JSON file (D5). One instance per process (singleton): it holds the in-memory copy of the file and
/// the lock that serialises every access to it.
/// <list type="bullet">
/// <item>All reads and writes go through one <see cref="SemaphoreSlim"/>, so two requests never interleave a write.</item>
/// <item>A write goes to a temporary file which then replaces the real one, so a crash mid-write leaves the old,
/// complete file — never half a JSON document.</item>
/// <item>The in-memory copy is replaced only after the file write succeeded, so memory and disk never disagree.</item>
/// </list>
/// Single-process only: two API instances would each have their own lock and cache (see README, "Scaling").
/// </summary>
public sealed class JsonTicketRepository(JsonStorageOptions options, ILogger<JsonTicketRepository> logger)
    : ITicketRepository, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _file = Path.Combine(options.DataDirectory, "tickets.json");

    // Null until first use; afterwards replaced as a whole (never mutated in place) after each successful write.
    private IReadOnlyList<Ticket>? _tickets;

    public async Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct)
    {
        var tickets = await SnapshotAsync(ct);
        return tickets.Where(query.Matches).ToList();
    }

    public async Task<Ticket?> GetAsync(Guid id, CancellationToken ct)
    {
        var tickets = await SnapshotAsync(ct);
        return tickets.FirstOrDefault(t => t.Id == id);
    }

    public async Task AddAsync(Ticket ticket, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await LoadIfNeededAsync(ct);
            var updated = current.Append(ticket).ToList();
            await WriteAsync(updated, ct);
            _tickets = updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TicketUpdate?> UpdateAsync(Guid id, Func<Ticket, Ticket> apply, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await LoadIfNeededAsync(ct);
            var index = current.ToList().FindIndex(t => t.Id == id);
            if (index < 0)
            {
                return null;
            }

            var before = current[index];
            var after = apply(before);
            if (ReferenceEquals(before, after))
            {
                return new TicketUpdate(before, after); // nothing changed — no write
            }

            var updated = current.ToList();
            updated[index] = after;
            await WriteAsync(updated, ct);
            _tickets = updated;
            return new TicketUpdate(before, after);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// The current list. Taken under the lock so a reader never sees a half-loaded cache; the list itself is never
    /// mutated afterwards (writers replace it), so it is safe to filter after the lock is released.
    /// </summary>
    private async Task<IReadOnlyList<Ticket>> SnapshotAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadIfNeededAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Loads the file on first use, seeding it from the dataset if it does not exist. Caller holds the lock.</summary>
    private async Task<IReadOnlyList<Ticket>> LoadIfNeededAsync(CancellationToken ct)
    {
        if (_tickets is not null)
        {
            return _tickets;
        }

        Directory.CreateDirectory(options.DataDirectory);

        if (!File.Exists(_file))
        {
            var seed = await ReadSeedAsync(ct);
            if (seed.Count > 0)
            {
                await WriteAsync(seed, ct);
                logger.LogInformation("Created {File} with {Count} tickets from the seed dataset", _file, seed.Count);
            }

            // With nothing to seed, no file is written yet: an empty file written now would make every later start
            // skip seeding, even after a wrong seed path is corrected. The file appears with the first real ticket.
            _tickets = seed;
            return seed;
        }

        _tickets = await ReadFileAsync(_file, ct);
        return _tickets;
    }

    private async Task<IReadOnlyList<Ticket>> ReadSeedAsync(CancellationToken ct)
    {
        if (options.SeedFile is null)
        {
            logger.LogInformation("No seed file configured; starting with no tickets");
            return [];
        }

        // A configured but missing seed is a deployment mistake (wrong path); starting empty would hide it.
        if (!File.Exists(options.SeedFile))
        {
            throw new FileNotFoundException(
                $"The seed file '{options.SeedFile}' (Storage:SeedFile) does not exist. Fix the path or remove the setting.",
                options.SeedFile);
        }

        return await ReadFileAsync(options.SeedFile, ct);
    }

    /// <summary>
    /// Reads and maps a tickets file. A file that cannot be read is an error, not "no tickets": treating it as empty
    /// would let the next save overwrite every customer's ticket with an empty list.
    /// </summary>
    private static async Task<IReadOnlyList<Ticket>> ReadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var records = await JsonSerializer.DeserializeAsync<List<TicketRecord>>(stream, SerializerOptions, ct) ?? [];
            return records.Select(r => r.ToDomain()).ToList();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new InvalidDataException(
                $"The tickets file '{path}' is not valid ({ex.Message}). Nothing was written to it; fix it and restart.", ex);
        }
    }

    /// <summary>Atomic replace: write a temporary file next to the real one, then move it over the real one.</summary>
    private async Task WriteAsync(IReadOnlyList<Ticket> tickets, CancellationToken ct)
    {
        var temp = _file + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, tickets.Select(TicketRecord.FromDomain).ToList(), SerializerOptions, ct);
            // Flush to the disk itself, not just the OS cache, before the rename makes this the real file.
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, _file, overwrite: true);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The file is data, never embedded in HTML, so characters such as ’ can stay readable instead of ’.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new UtcTimestampConverter() },
    };

    /// <summary>
    /// The on-disk shape, identical to the provided <c>dataset.json</c>. Separate from the domain record so that the
    /// file format (status spelled "In Progress", string ids) is this adapter's business, not Core's.
    /// </summary>
    private sealed record TicketRecord(
        Guid Id, string Name, string Email, string Description, string? Summary, string? ImageUrl,
        string Status, string? Resolution, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        public static TicketRecord FromDomain(Ticket t) => new(
            t.Id, t.Name, t.Email, t.Description, t.Summary, t.ImageUrl,
            t.Status.ToName(), t.Resolution, t.CreatedAt, t.UpdatedAt);

        // The JSON reader does not enforce non-nullable strings; a record with a missing name must be reported here,
        // where the file is named in the error, not fail later somewhere in Core.
        public Ticket ToDomain() => new()
        {
            Id = Id,
            Name = Required(Name, "name"),
            Email = Required(Email, "email"),
            Description = Required(Description, "description"),
            Summary = Summary,
            ImageUrl = ImageUrl,
            Status = TicketStatusNames.TryParse(Status, out var status)
                ? status
                : throw new FormatException($"Unknown status '{Status}' in ticket {Id}"),
            Resolution = Resolution ?? "",
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

        private string Required(string? value, string field) =>
            string.IsNullOrWhiteSpace(value) ? throw new FormatException($"Ticket {Id} has no {field}") : value;
    }

    /// <summary>
    /// Writes timestamps as UTC with a "Z", as in the dataset (<c>2025-10-27T14:35:00Z</c>); the default would write
    /// <c>+00:00</c> and needless fractional zeros, changing every line of the file on the first save.
    /// </summary>
    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
