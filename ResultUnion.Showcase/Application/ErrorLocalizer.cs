using System.Text.RegularExpressions;

namespace ResultUnion.Showcase.Application;

/// <summary>
/// What a client (or a server-side localization layer) does with <see cref="Error.Code"/> and <see cref="Error.Arguments"/>:
/// look up a template for the code in the user's language and fill the placeholders from the arguments.
/// </summary>
/// <remarks>In a real application the templates come from resource files or a translation service.</remarks>
public static partial class ErrorLocalizer
{
    private static readonly Dictionary<string, string> Georgian = new(StringComparer.Ordinal)
    {
        ["email.required"] = "ელფოსტა სავალდებულოა.",
        ["email.invalid_format"] = "„{value}“ არ არის სწორი ელფოსტა.",
        ["password.too_short"] = "პაროლი უნდა შედგებოდეს მინიმუმ {minimumLength} სიმბოლოსგან.",
        ["user.age_not_a_number"] = "„{input}“ არ არის რიცხვი.",
        ["user.age_out_of_range"] = "ასაკი {age} უნდა იყოს {minimum}-დან {maximum}-მდე.",
        ["user.too_young"] = "მომხმარებელი უნდა იყოს მინიმუმ {minimumAge} წლის, არის {age}.",
        ["user.email_already_registered"] = "ელფოსტა {email} უკვე რეგისტრირებულია.",
        ["order_line.product_required"] = "პროდუქტი სავალდებულოა.",
        ["order_line.quantity_not_positive"] = "რაოდენობა უნდა იყოს დადებითი, მითითებულია {quantity}.",
    };

    /// <summary>Returns the Georgian message for the error, or its English <see cref="Error.Message"/> when there is no template.</summary>
    public static string ToGeorgian(Error error) =>
        Georgian.TryGetValue(error.Code, out var template)
            ? Placeholder().Replace(template, match =>
                error.Arguments.TryGetValue(match.Groups["name"].Value, out var value) ? $"{value}" : match.Value)
            : error.Message;

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}")]
    private static partial Regex Placeholder();
}
