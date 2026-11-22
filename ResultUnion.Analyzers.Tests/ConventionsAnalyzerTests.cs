using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class InlineErrorTests
{
    private static async Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) =>
        [.. (await AnalyzeAsync(source, new ConventionsAnalyzer())).Where(d => d.Id == "RU0006")];

    [Fact]
    public async Task Errors_created_in_a_handler_are_reported()
    {
        var diagnostics = await Analyze("""
            class Handler
            {
                Result<int> Parse(string s) => s.Length > 0 ? 1 : Error.Create("number.required", "Required.", ErrorType.Validation);
                Result Check(int n) => n > 0 ? Result.Success() : Error.Validation("Must be positive.");
                Result<int> Find(Result<int> r) => r.MapError(_ => Error.NotFound("Missing."));
            }
            """);

        Assert.Equal([("RU0006", 3), ("RU0006", 4), ("RU0006", 5)], diagnostics);
    }

    [Fact]
    public async Task Errors_in_a_catalog_are_fine()
    {
        var diagnostics = await Analyze("""
            sealed record Email
            {
                public static class Errors
                {
                    public static readonly Error Required = Error.Create("email.required", "Required.", ErrorType.Validation);
                    public static Error InvalidFormat(string value) =>
                        Error.Create("email.invalid_format", "Invalid.", ErrorType.Validation).WithArgument("value", value);
                }
            }

            static class OrderErrors
            {
                public static Error NotFound(int id) => Error.NotFound($"No order {id}.");
                static class Nested { static readonly Error Deep = Error.Conflict("Deep."); }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Translating_another_system_s_code_is_fine()
    {
        var diagnostics = await Analyze("""
            class Client
            {
                // The code comes from the response: nothing is invented here.
                Error Translate(string code, string message) => Error.Create(code, message, ErrorType.Conflict);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Grouping_existing_errors_is_fine()
    {
        var diagnostics = await Analyze("""
            class Handler
            {
                Error Group(Error a, Error b) => Error.Validation([a, b]);
            }
            """);

        Assert.Empty(diagnostics);
    }
}

public sealed class FieldSetTooEarlyTests
{
    private static async Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) =>
        [.. (await AnalyzeAsync(source, new ConventionsAnalyzer())).Where(d => d.Id == "RU0007")];

    [Fact]
    public async Task WithField_in_a_single_input_value_object_is_reported()
    {
        var diagnostics = await Analyze("""
            sealed record Email(string Value)
            {
                static readonly Error Required = Error.Validation("Required.");

                public static Result<Email> Create(string? input) =>
                    string.IsNullOrWhiteSpace(input) ? Required.WithField("email") : new Email(input);

                public static Task<Result<Email>> CreateAsync(string? input) =>
                    Task.FromResult(Create(input).MapError(e => e.WithField("email")));
            }
            """);

        Assert.Equal([("RU0007", 6), ("RU0007", 9)], diagnostics);
    }

    [Fact]
    public async Task A_nullable_simple_input_is_one_input_too()
    {
        var diagnostics = await Analyze("""
            sealed record Age(int Value)
            {
                static readonly Error Required = Error.Validation("Required.");

                public static Result<Age> Create(int? input) => input is { } value ? new Age(value) : Required.WithField("age");
            }
            """);

        Assert.Equal([("RU0007", 5)], diagnostics);
    }

    [Fact]
    public async Task WithField_in_an_error_catalog_is_reported()
    {
        var diagnostics = await Analyze("""
            static class ReasonErrors
            {
                public static readonly Error Required = Error.Validation("Required.").WithField("reason");
            }
            """);

        Assert.Equal([("RU0007", 3)], diagnostics);
    }

    [Fact]
    public async Task WithField_where_the_field_name_is_known_is_fine()
    {
        var diagnostics = await Analyze("""
            sealed record OrderLine(string Product, int Quantity)
            {
                static readonly Error Required = Error.Validation("Required.");

                // Several inputs: the value object names its own parts.
                public static Result<OrderLine> Create(string? product, int quantity) =>
                    string.IsNullOrEmpty(product) ? Required.WithField("product") : new OrderLine(product, quantity);
            }

            sealed record Email(string Value)
            {
                public static Result<Email> Create(string? input) => new Email(input ?? "");
            }

            sealed class Handler
            {
                // The handler reads the request, so it knows what the input is called.
                public static Result<Email> Register(string? email) => Email.Create(email).MapError(e => e.WithField("email"));

                // Outside any method: no factory to blame.
                static readonly Func<Error, Error> Named = e => e.WithField("email");
            }
            """);

        Assert.Empty(diagnostics);
    }
}

public sealed class ValueTaskResultTests
{
    private static async Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) =>
        [.. (await AnalyzeAsync(source, new ConventionsAnalyzer())).Where(d => d.Id == "RU0008")];

    [Fact]
    public async Task Methods_local_functions_and_properties_that_return_a_ValueTask_of_a_result_are_reported()
    {
        var diagnostics = await Analyze("""
            class Handler
            {
                ValueTask<Result<int>> Find() => ValueTask.FromResult(Result<int>.Success(1));
                ValueTask<Result> Check() => ValueTask.FromResult(Result.Success());
                ValueTask<Result> Pending => ValueTask.FromResult(Result.Success());
                ValueTask<Result> Later { get => ValueTask.FromResult(Result.Success()); }

                async Task<int> M()
                {
                    return (await Local()).ResultValue;
                    static ValueTask<Result<int>> Local() => ValueTask.FromResult(Result<int>.Success(1));
                }
            }
            """);

        Assert.Equal([("RU0008", 3), ("RU0008", 4), ("RU0008", 5), ("RU0008", 6), ("RU0008", 11)], diagnostics);
    }

    [Fact]
    public async Task Given_signatures_and_other_ValueTasks_are_fine()
    {
        var diagnostics = await Analyze("""
            interface IStore { ValueTask<Result<int>> FindAsync(); }

            class Store : IStore
            {
                public ValueTask<Result<int>> FindAsync() => ValueTask.FromResult(Result<int>.Success(1));
            }

            abstract class Base { public abstract ValueTask<Result> SaveAsync(); }

            class Derived : Base
            {
                public override ValueTask<Result> SaveAsync() => ValueTask.FromResult(Result.Success());
            }

            class Other
            {
                ValueTask<int> Count() => ValueTask.FromResult(1);
                Task<Result<int>> Find() => Task.FromResult(Result<int>.Success(1));
            }
            """);

        // The interface and the abstract member declare the signature themselves, so they are reported; the
        // implementations, which only follow it, are not.
        Assert.Equal([("RU0008", 1), ("RU0008", 8)], diagnostics);
    }
}
