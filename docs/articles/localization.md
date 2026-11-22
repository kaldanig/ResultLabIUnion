# Translating error messages

An error's code is a contract: clients switch on it, and it never changes with the language. Its message is the text
for a developer or a log, in the language the code was written in. A message in the user's language is made from the
code and the error's arguments, by whoever knows that language: the server, for the request's culture, or the client.
Both ways use the same pieces, and this page builds them with `IStringLocalizer` and resource files. The code is in
`ResultUnion.Articles` (`ErrorMessages.cs`, `Program.cs`), and `ResultUnion.Articles.Tests` runs it in Georgian.

## Why the arguments matter

A catalog error carries the values its message is made of:

```csharp
        public static Error TooShort(int minimumLength) =>
            Error.Create("password.too_short", $"Password must be at least {minimumLength} characters.", ErrorType.Validation)
                .WithArgument("minimumLength", minimumLength);
```

The English message has the 8 in it, but a translation cannot take it from there. `WithArgument` puts it next to the
code, where a translation finds it by name, and a problem response sends it along (`arguments`, and per entry in
`details`). An error whose message has a value in it and no argument for it cannot be translated well: add the
argument in the catalog.

## Messages by code

The translations live in a resource file per language, one entry per code, with the arguments as `{name}`
placeholders. `ErrorMessages.ka.resx` has the Georgian ones:

```xml
<data name="password.too_short" xml:space="preserve"><value>პაროლი უნდა იყოს მინიმუმ {minimumLength} სიმბოლო.</value></data>
<data name="email.required" xml:space="preserve"><value>ელფოსტა სავალდებულოა.</value></data>
```

The neutral `ErrorMessages.resx` is empty: in English, an error keeps the message it was created with, so the catalog
stays the one place where the English text is written. A code with no entry for a language falls back the same way.

One class turns an error into its message for the current UI culture:

```csharp
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
```

## On the server: the problem response in the request's language

The domain and the use cases do not change: they create errors with English messages, as before. The translation
happens where the error becomes a problem response. `ToHttpResult` writes the response through ASP.NET Core's
problem details service, so `CustomizeProblemDetails` sees every problem on its way out, with the `code` and the
`arguments` the error put there:

```csharp
builder.Services.AddLocalization();
builder.Services.AddSingleton<ErrorMessages>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Translate(context.HttpContext.RequestServices.GetRequiredService<ErrorMessages>()));
```

```csharp
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ka")
    .AddSupportedUICultures("en", "ka"));
```

`Translate` replaces the texts and leaves the codes alone: `detail` from the top-level `code`, each entry of
`details` from its own code and arguments, and the messages per field in `errors`:

```csharp
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
```

The registration from [Getting started](getting-started.md), sent with `Accept-Language: ka`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "ერთი ან მეტი ველი არასწორია.",
  "errors": {
    "email": [ "ელფოსტა სავალდებულოა." ],
    "password": [ "პაროლი უნდა იყოს მინიმუმ 8 სიმბოლო." ]
  },
  "code": "validation.failed",
  "category": "Validation",
  "details": [
    { "code": "email.required", "field": "email", "message": "ელფოსტა სავალდებულოა." },
    { "code": "password.too_short", "field": "password", "message": "პაროლი უნდა იყოს მინიმუმ 8 სიმბოლო.", "arguments": { "minimumLength": 8 } }
  ]
}
```

Without the header, or with a language that has no resource file, the response is the English one that Getting
started shows. The `title` is ASP.NET Core's and stays as it is; translate it in the same callback if a client shows
it. An `Unexpected` error has no arguments in its response and a fixed `detail` (DESIGN.md, the problem details
mapping): give its code a translation, or leave the generic text.

## On the client: translate what arrives

A client that has its own resources, a mobile or a desktop app, can take the English response and translate it
itself, for the language its user chose. `ReadResultAsync` (ResultUnion.Http) turns the problem back into an `Error`
with the same codes, fields and arguments, so the same `Describe` works on it:

```csharp
            var result = await http.PostAsJsonAsync("/users", Invalid, Ct).ReadResultAsync<UserResponse>(Ct);
            if (result.TryGetError(out var error))
            {
                foreach (var detail in error.Details)
                    Show(detail.Field, messages.Describe(detail));
            }
```

An argument that came over JSON is a `JsonElement`; `Convert.ToString` writes its value as it was sent, which is what
a placeholder needs for numbers and text. Format dates and amounts for the culture before you add them as arguments,
or let the client parse the element and format it.

## Choosing where

- **The server** when the clients only show what they receive (a web front end, a third party): one set of
  resources, and every client gets the user's language from `Accept-Language`.
- **The client** when it has its own resources or works offline: the server stays in one language, and the codes and
  arguments are the whole contract.

Either way, a test per language pins the translations to the codes, as `LocalizationArticle` does: a renamed code
without its resource falls back to English, and the test shows it.
