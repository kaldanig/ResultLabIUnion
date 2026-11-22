using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ResultUnion.AspNetCore;

namespace ResultUnion.Shop.Orders.Localization;

/// <summary>
/// Puts a problem response's messages into the request's language, from resources keyed by error code
/// (<c>ProblemTranslator.ka.resx</c>), with the error's arguments in <c>{name}</c> placeholders. Codes never change;
/// a code with no resource keeps the English message the domain wrote. docs/articles/localization.md explains the idea.
/// </summary>
public sealed class ProblemTranslator(IStringLocalizer<ProblemTranslator> resources)
{
    public void Translate(ProblemDetails problem)
    {
        if (Extension(problem, "code") is string code)
            problem.Detail = Message(code, Extension(problem, "arguments") as IReadOnlyDictionary<string, object?>) ?? problem.Detail;

        if (Extension(problem, "details") is not ProblemErrorDetail[] details)
            return;

        var translated = Array.ConvertAll(details, detail => detail with { Message = Message(detail.Code, detail.Arguments) ?? detail.Message });
        problem.Extensions["details"] = translated;

        if (problem is HttpValidationProblemDetails validation)
        {
            validation.Errors.Clear();
            foreach (var byField in translated.ToLookup(detail => detail.Field ?? string.Empty))
                validation.Errors[byField.Key] = [.. byField.Select(detail => detail.Message)];
        }
    }

    private static object? Extension(ProblemDetails problem, string name) =>
        problem.Extensions.TryGetValue(name, out var value) ? value : null;

    private string? Message(string code, IReadOnlyDictionary<string, object?>? arguments)
    {
        var resource = resources[code];
        return resource.ResourceNotFound ? null : Fill(resource.Value, arguments);
    }

    // "{available}" becomes the argument of that name; a placeholder without an argument stays as it is.
    private static string Fill(string template, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return template;

        var text = new StringBuilder(template);
        foreach (var (name, value) in arguments)
            text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.CurrentCulture));

        return text.ToString();
    }
}
