using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Tests.Fakes;

/// <summary>A repository in a list, with the same "update is one atomic step" contract as the real one.</summary>
public sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<Ticket> _tickets = [];

    public int Writes { get; private set; }

    public InMemoryTicketRepository(params Ticket[] seed) => _tickets.AddRange(seed);

    public IReadOnlyList<Ticket> All => _tickets.ToList();

    public Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Ticket>>(_tickets.Where(query.Matches).ToList());

    public Task<Ticket?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_tickets.FirstOrDefault(t => t.Id == id));

    public async Task AddAsync(Ticket ticket, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _tickets.Add(ticket);
            Writes++;
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
            var index = _tickets.FindIndex(t => t.Id == id);
            if (index < 0)
            {
                return null;
            }

            var before = _tickets[index];
            var after = apply(before);
            if (!ReferenceEquals(before, after))
            {
                _tickets[index] = after;
                Writes++;
            }

            return new TicketUpdate(before, after);
        }
        finally
        {
            _lock.Release();
        }
    }
}
