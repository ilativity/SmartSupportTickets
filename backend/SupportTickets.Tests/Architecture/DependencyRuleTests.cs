using System.Reflection;
using SupportTickets.Core.Domain;

namespace SupportTickets.Tests.Architecture;

/// <summary>
/// The hexagon's dependency rule, checked on the compiled assemblies: Core must not depend on any web framework,
/// email library or HTTP client — those belong in adapters. A project reference added by mistake fails this test.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly CoreAssembly = typeof(Ticket).Assembly;

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("MailKit")]
    [InlineData("MimeKit")]
    [InlineData("System.Net.Http")]
    [InlineData("System.Text.Json")]
    [InlineData("SupportTickets.Adapters")]
    [InlineData("SupportTickets.Api")]
    public void Core_does_not_reference(string forbiddenPrefix)
    {
        var offending = CoreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Adapters_do_not_reference_the_web_framework()
    {
        var adapters = Assembly.Load("SupportTickets.Adapters");

        Assert.DoesNotContain(adapters.GetReferencedAssemblies(),
            a => (a.Name ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
}
