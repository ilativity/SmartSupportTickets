namespace SupportTickets.Core.Ports.Out;

/// <summary>Driven port: new ticket ids. A port rather than an inline <c>Guid.NewGuid()</c> so tests can predict ids (D20).</summary>
public interface IIdGenerator
{
    Guid NewId();
}
