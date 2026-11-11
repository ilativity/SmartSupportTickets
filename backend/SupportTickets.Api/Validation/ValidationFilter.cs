using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SupportTickets.Api.Validation;

/// <summary>
/// Runs DataAnnotations on the endpoint's <typeparamref name="T"/> argument. Minimal APIs (in .NET 9) do not validate
/// on their own; this returns the standard RFC 7807 validation problem — <c>{ errors: { name: [...] } }</c> — which the
/// frontend shows next to each field.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class, new()
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // A form with none of the expected fields binds to null, not to an empty object. Validating an empty
        // instance instead gives the client the same per-field errors as a form with every field left blank.
        var argument = context.Arguments.OfType<T>().FirstOrDefault() ?? new T();

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
        {
            var errors = results
                .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(member => (member, r.ErrorMessage ?? "Invalid value.")))
                .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.member))
                .ToDictionary(g => g.Key, g => g.Select(e => e.Item2).ToArray());
            return TypedResults.ValidationProblem(errors);
        }

        return await next(context);
    }
}
