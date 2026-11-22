using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ResultUnion.AspNetCore;

namespace ResultUnion.Articles;

// docs/articles/localization.md: messages in the user's language, looked up by error code. The resources are
// ErrorMessages.ka.resx (Georgian) and an empty ErrorMessages.resx: English is the message an error is created with.

/// <summary>An error's message in the current UI culture: the resource named by its code, with its arguments filled in.</summary>
public sealed partial class ErrorMessages(IStringLocalizer<ErrorMessages> localizer)
{
    public string Describe(Error error) => Describe(error.Code, error.Arguments, error.Message);

    public string Describe(string code, IReadOnlyDictionary<string, object?>? arguments, string fallback)
    {
        var template = localizer[code];
        if (template.ResourceNotFound)
            return fallback;  // no translation for this code: the message the error was created with

        // "{minimumLength}" in the resource becomes the argument of that name, formatted for the culture.
        return Placeholder().Replace(template.Value, match =>
            arguments is not null && arguments.TryGetValue(match.Groups["name"].Value, out var value)
                ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
                : match.Value);
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}")]
    private static partial Regex Placeholder();
}

/// <summary>Translates a problem response on its way out, from the <c>code</c> and <c>arguments</c> it carries.</summary>
public static class ProblemTranslation
{
    public static void Translate(this ProblemDetails problem, ErrorMessages messages)
    {
        if (problem.Extensions.TryGetValue("code", out var value) && value is string code)
        {
            var arguments = problem.Extensions.TryGetValue("arguments", out var found) ? found as IReadOnlyDictionary<string, object?> : null;
            problem.Detail = messages.Describe(code, arguments, problem.Detail ?? string.Empty);
        }

        if (problem.Extensions.TryGetValue("details", out var entries) && entries is ProblemErrorDetail[] details)
        {
            var translated = details.Select(detail => detail with { Message = messages.Describe(detail.Code, detail.Arguments, detail.Message) }).ToArray();
            problem.Extensions["details"] = translated;

            if (problem is HttpValidationProblemDetails validation)
            {
                foreach (var field in translated.GroupBy(detail => detail.Field ?? string.Empty, StringComparer.Ordinal))
                    validation.Errors[field.Key] = [.. field.Select(detail => detail.Message)];
            }
        }
    }
}
