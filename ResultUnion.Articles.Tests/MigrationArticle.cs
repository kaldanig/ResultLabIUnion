// docs/articles/migration.md: the same value objects and use case written with FluentResults and with ErrorOr, the
// adapters that let old code and new code meet, and tests that the ResultUnion version gives the same answers.
// Each library's code is in a namespace block with its own using, so its Result and Error win over ResultUnion's.

namespace ResultUnion.Articles.Tests.Before.WithFluentResults
{
    using FluentResults;

    public sealed record Email(string Value)
    {
        public static Result<Email> Create(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Result.Fail(new Error("Email is required.").WithMetadata("code", "email.required"));

            var trimmed = input.Trim();
            return trimmed.Contains('@')
                ? Result.Ok(new Email(trimmed.ToLowerInvariant()))
                : Result.Fail(new Error($"'{trimmed}' is not a valid email.").WithMetadata("code", "email.invalid_format"));
        }
    }

    public sealed record Password(string Value)
    {
        public static Result<Password> Create(string? input) =>
            input is { Length: >= 8 }
                ? Result.Ok(new Password(input))
                : Result.Fail(new Error("Password must be at least 8 characters.").WithMetadata("code", "password.too_short"));
    }

    public sealed record Account(Email Email, Password Password)
    {
        public static Result<Account> Register(string? email, string? password)
        {
            var emailResult = Email.Create(email);
            var passwordResult = Password.Create(password);
            var merged = Result.Merge(emailResult, passwordResult);  // every failure, as a list of errors
            if (merged.IsFailed)
                return merged.ToResult<Account>();

            return Result.Ok(new Account(emailResult.Value, passwordResult.Value));
        }
    }
}

namespace ResultUnion.Articles.Tests.Before.WithErrorOr
{
    using ErrorOr;

    public sealed record Email(string Value)
    {
        public static ErrorOr<Email> Create(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Error.Validation("email.required", "Email is required.");

            var trimmed = input.Trim();
            return trimmed.Contains('@')
                ? new Email(trimmed.ToLowerInvariant())
                : Error.Validation("email.invalid_format", $"'{trimmed}' is not a valid email.");
        }
    }

    public sealed record Password(string Value)
    {
        public static ErrorOr<Password> Create(string? input) =>
            input is { Length: >= 8 }
                ? new Password(input)
                : Error.Validation("password.too_short", "Password must be at least 8 characters.");
    }

    public sealed record Account(Email Email, Password Password)
    {
        public static ErrorOr<Account> Register(string? email, string? password)
        {
            var emailResult = Email.Create(email);
            var passwordResult = Password.Create(password);
            List<Error> errors = [.. emailResult.ErrorsOrEmptyList, .. passwordResult.ErrorsOrEmptyList];
            if (errors.Count > 0)
                return errors;

            return new Account(emailResult.Value, passwordResult.Value);
        }
    }
}

namespace ResultUnion.Articles.Tests.Migration
{
    using FR = FluentResults;
    using EO = ErrorOr;

    /// <summary>
    /// Results of the old library as ResultUnion results, for the code that is not migrated yet. One error stays one
    /// error; several become one grouped error with them as details, as <c>CombineAll</c> would report them.
    /// </summary>
    public static class LegacyResults
    {
        // FluentResults has no category: the caller says what the failures are.
        public static Result<T> ToResultUnion<T>(this FR.Result<T> result, ErrorType type) where T : notnull =>
            result.IsSuccess ? result.Value : Group(result.Errors.Select(error => FromFluentResults(error, type)).ToList());

        public static Result<T> ToResultUnion<T>(this EO.ErrorOr<T> result) where T : notnull =>
            result.IsError ? Group(result.Errors.Select(FromErrorOr).ToList()) : result.Value;

        private static Error FromFluentResults(FR.IError error, ErrorType type) =>
            Error.Create(error.Metadata.TryGetValue("code", out var code) && code is string text ? text : "legacy.error", error.Message, type);

        private static Error FromErrorOr(EO.Error error) =>
            Error.Create(error.Code, error.Description, error.Type switch
            {
                EO.ErrorType.Validation => ErrorType.Validation,
                EO.ErrorType.NotFound => ErrorType.NotFound,
                EO.ErrorType.Conflict => ErrorType.Conflict,
                EO.ErrorType.Unauthorized => ErrorType.Unauthorized,
                EO.ErrorType.Forbidden => ErrorType.Forbidden,
                EO.ErrorType.Failure => ErrorType.BusinessRule,  // a failure the caller can understand
                _ => ErrorType.Unexpected,
            });

        private static Error Group(List<Error> errors) => errors.Count == 1 ? errors[0] : Error.Validation(errors);
    }
}

namespace ResultUnion.Articles.Tests
{
    using ResultUnion.Articles.Tests.Migration;
    using ResultUnion.Testing;
    using FluentAccount = ResultUnion.Articles.Tests.Before.WithFluentResults.Account;
    using ErrorOrAccount = ResultUnion.Articles.Tests.Before.WithErrorOr.Account;

    public sealed class MigrationArticle
    {
        public static TheoryData<string?, string?> Inputs => new()
        {
            { "ann@example.com", "long enough" },
            { "", "short" },
            { "not-an-email", "long enough" },
        };

        // The same input gives the same codes with every library: the migration changes the code, not the contract.
        [Theory]
        [MemberData(nameof(Inputs))]
        public void Every_version_gives_the_same_codes(string? email, string? password)
        {
            var expected = Codes(Result.CombineAll(Email.Create(email), Password.Create(password)).Map((e, p) => (e, p)));

            Assert.Equal(expected, Codes(FluentAccount.Register(email, password).ToResultUnion(ErrorType.Validation)));
            Assert.Equal(expected, Codes(ErrorOrAccount.Register(email, password).ToResultUnion()));
        }

        [Fact]
        public void One_legacy_error_stays_one_error_and_keeps_its_category()
        {
            var error = ErrorOrAccount.Register("", "long enough").ToResultUnion().ShouldFail("email.required");

            Assert.Equal(ErrorType.Validation, error.Type);
            Assert.Empty(error.Details);
        }

        private static string[] Codes<T>(Result<T> result) where T : notnull =>
            result.TryGetError(out var error)
                ? (error.Details.Count > 0 ? [.. error.Details.Select(detail => detail.Code)] : [error.Code])
                : [];
    }
}
