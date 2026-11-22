using BenchmarkDotNet.Attributes;

// The same use case, an email and a password validated together with every failure reported, written the way each
// library's documentation writes it (docs/BENCHMARKS.md, "Against other libraries"). Each version is in a namespace
// block with its own using, so its Result and Error win over ResultUnion's. The logic and the messages are the same in
// all four: an email needs an '@', a password 8 characters.

namespace ResultUnion.Benchmarks
{
    public class ComparisonBenchmarks
    {
        [Params("valid", "one invalid", "two invalid")]
        public string Input { get; set; } = "valid";

        private string _email = "";
        private string _password = "";

        [GlobalSetup]
        public void Setup() => (_email, _password) = Input switch
        {
            "valid" => ("ann@example.com", "long enough"),
            "one invalid" => ("ann@example.com", "short"),
            _ => ("not-an-email", "short"),
        };

        [Benchmark(Baseline = true, Description = "ResultUnion: CombineAll")]
        public int ResultUnion() =>
            WithResultUnion.Account.Register(_email, _password).Match(static account => account.Email.Length, static error => error.Details.Count + 1);

        [Benchmark(Description = "FluentResults: Result.Merge")]
        public int FluentResults()
        {
            var result = WithFluentResults.Account.Register(_email, _password);
            return result.IsSuccess ? result.Value.Email.Length : result.Errors.Count;
        }

        [Benchmark(Description = "ErrorOr: a list of errors")]
        public int ErrorOr()
        {
            var result = WithErrorOr.Account.Register(_email, _password);
            return result.IsError ? result.Errors.Count : result.Value.Email.Length;
        }

        // An exception stops at the first failure: it cannot report both, which is part of why results exist.
        [Benchmark(Description = "exceptions: throw at the first failure")]
        public int Exceptions()
        {
            try
            {
                return WithExceptions.Account.Register(_email, _password).Email.Length;
            }
            catch (ArgumentException exception)
            {
                return exception.Message.Length;
            }
        }
    }
}

namespace ResultUnion.Benchmarks.WithResultUnion
{
    public sealed record Email(string Value)
    {
        public static class Errors
        {
            public static Error InvalidFormat(string value) =>
                Error.Create("email.invalid_format", $"'{value}' is not a valid email.", ErrorType.Validation).WithArgument("value", value);
        }

        public static Result<Email> Create(string input) => input.Contains('@') ? new Email(input) : Errors.InvalidFormat(input);
    }

    public sealed record Password(string Value)
    {
        public static class Errors
        {
            public static readonly Error TooShort =
                Error.Create("password.too_short", "Password must be at least 8 characters.", ErrorType.Validation);
        }

        public static Result<Password> Create(string input) => input.Length >= 8 ? new Password(input) : Errors.TooShort;
    }

    public sealed record Account(string Email, string Password)
    {
        public static Result<Account> Register(string email, string password) =>
            Result.CombineAll(WithResultUnion.Email.Create(email), WithResultUnion.Password.Create(password))
                .Map(static (e, p) => new Account(e.Value, p.Value));
    }
}

namespace ResultUnion.Benchmarks.WithFluentResults
{
    using FluentResults;

    public sealed record Email(string Value)
    {
        public static Result<Email> Create(string input) =>
            input.Contains('@')
                ? Result.Ok(new Email(input))
                : Result.Fail(new Error($"'{input}' is not a valid email.").WithMetadata("code", "email.invalid_format"));
    }

    public sealed record Password(string Value)
    {
        public static Result<Password> Create(string input) =>
            input.Length >= 8
                ? Result.Ok(new Password(input))
                : Result.Fail(new Error("Password must be at least 8 characters.").WithMetadata("code", "password.too_short"));
    }

    public sealed record Account(string Email, string Password)
    {
        public static Result<Account> Register(string email, string password)
        {
            var emailResult = WithFluentResults.Email.Create(email);
            var passwordResult = WithFluentResults.Password.Create(password);
            var merged = Result.Merge(emailResult, passwordResult);
            return merged.IsFailed
                ? merged.ToResult<Account>()
                : Result.Ok(new Account(emailResult.Value.Value, passwordResult.Value.Value));
        }
    }
}

namespace ResultUnion.Benchmarks.WithErrorOr
{
    using ErrorOr;

    public sealed record Email(string Value)
    {
        public static ErrorOr<Email> Create(string input) =>
            input.Contains('@') ? new Email(input) : Error.Validation("email.invalid_format", $"'{input}' is not a valid email.");
    }

    public sealed record Password(string Value)
    {
        public static ErrorOr<Password> Create(string input) =>
            input.Length >= 8 ? new Password(input) : Error.Validation("password.too_short", "Password must be at least 8 characters.");
    }

    public sealed record Account(string Email, string Password)
    {
        public static ErrorOr<Account> Register(string email, string password)
        {
            var emailResult = WithErrorOr.Email.Create(email);
            var passwordResult = WithErrorOr.Password.Create(password);
            if (emailResult.IsError || passwordResult.IsError)
                return (List<Error>)[.. emailResult.ErrorsOrEmptyList, .. passwordResult.ErrorsOrEmptyList];

            return new Account(emailResult.Value.Value, passwordResult.Value.Value);
        }
    }
}

namespace ResultUnion.Benchmarks.WithExceptions
{
    public sealed record Account(string Email, string Password)
    {
        public static Account Register(string email, string password)
        {
            if (!email.Contains('@'))
                throw new ArgumentException($"'{email}' is not a valid email.", nameof(email));
            if (password.Length < 8)
                throw new ArgumentException("Password must be at least 8 characters.", nameof(password));

            return new Account(email, password);
        }
    }
}
