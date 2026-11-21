using Microsoft.AspNetCore.Http.HttpResults;
using ResultUnion.AspNetCore;
using ResultUnion.Samples.Domain.Localization;

namespace ResultUnion.WebApi.Localization;

/// <summary>
/// Translates problem responses for <c>Accept-Language: ka</c>, using only what the response carries: the error
/// <c>code</c> and its <c>arguments</c>. This is why errors have codes and arguments: the English message is never parsed.
/// </summary>
/// <remarks>Codes without a Georgian template keep their English message. Unexpected errors keep their fixed message.</remarks>
public sealed class AcceptLanguageProblemFilter : IEndpointFilter
{
    private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (!context.HttpContext.Request.Headers.AcceptLanguage.ToString().StartsWith("ka", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        // Typed results wrap the chosen response: Results<Created<UserResponse>, ProblemHttpResult>.
        var problem = (result as INestedHttpResult)?.Result as ProblemHttpResult ?? result as ProblemHttpResult;

        if (problem?.ProblemDetails is { } details && details.Extensions.TryGetValue("code", out var code) && code is string errorCode)
        {
            context.HttpContext.Response.Headers.ContentLanguage = "ka";

            var arguments = details.Extensions.TryGetValue("arguments", out var value) && value is IReadOnlyDictionary<string, object?> found
                ? found
                : NoArguments;
            details.Detail = ErrorLocalizer.ToGeorgian(errorCode, arguments, details.Detail ?? string.Empty);

            if (details.Extensions.TryGetValue("details", out var list) && list is ProblemErrorDetail[] entries)
            {
                var translated = entries
                    .Select(entry => entry with { Message = ErrorLocalizer.ToGeorgian(entry.Code, entry.Arguments ?? NoArguments, entry.Message) })
                    .ToArray();

                details.Extensions["details"] = translated;

                if (details is HttpValidationProblemDetails validation)
                {
                    validation.Errors.Clear();
                    foreach (var group in translated.GroupBy(entry => entry.Field ?? string.Empty, StringComparer.Ordinal))
                    {
                        validation.Errors[group.Key] = [.. group.Select(entry => entry.Message)];
                    }
                }
            }
        }

        return result;
    }
}
