using System.Text.RegularExpressions;

namespace ResultUnion.Samples.Domain.Localization;

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
        ["order.no_lines"] = "შეკვეთას უნდა ჰქონდეს მინიმუმ ერთი ხაზი.",
        ["user.not_found"] = "მომხმარებელი {email} ვერ მოიძებნა.",
        ["user.already_inactive"] = "მომხმარებელი {email} უკვე დეაქტივირებულია.",
        ["user.password_reset_limit"] = "პაროლის აღდგენის ლიმიტი ამოიწურა. სცადეთ {retryAfterSeconds} წამში.",
        ["validation.failed"] = "ერთი ან მეტი ველი არასწორია.",
        ["forbidden"] = "ამ მოქმედების უფლება არ გაქვთ.",
    };

    /// <summary>Returns the Georgian message for the error, or its English <see cref="Error.Message"/> when there is no template.</summary>
    public static string ToGeorgian(Error error) => ToGeorgian(error.Code, error.Arguments, error.Message);

    /// <summary>
    /// Returns the Georgian message for <paramref name="code"/>, filled from <paramref name="arguments"/>, or
    /// <paramref name="fallback"/> when there is no template. Works on what a problem response carries (<c>code</c>, <c>arguments</c>).
    /// </summary>
    public static string ToGeorgian(string code, IReadOnlyDictionary<string, object?> arguments, string fallback) =>
        Georgian.TryGetValue(code, out var template)
            ? Placeholder().Replace(template, match =>
                arguments.TryGetValue(match.Groups["name"].Value, out var value) ? $"{value}" : match.Value)
            : fallback;

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}")]
    private static partial Regex Placeholder();
}
