using ResultUnion.CodeFixes;
using static ResultUnion.Analyzers.Tests.CodeFixHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class DiscardResultCodeFixTests
{
    [Fact]
    public async Task A_discarded_result_gets_an_explicit_discard()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M()
                {
                    // keep this comment
                    Parse("1");
                }
            }
            """, new DiscardResultCodeFix());

        Assert.Equal("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M()
                {
                    // keep this comment
                    _ = Parse("1");
                }
            }
            """, fixedCode.Code);
        Assert.Equal([DiscardResultCodeFix.Title], fixedCode.Titles);
    }

    [Fact]
    public async Task An_awaited_result_and_a_lambda_body_are_discarded_too()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Task<Result> SaveAsync() => Task.FromResult(Result.Success());
                Result Validate(int x) => Result.Success();
                void Run(Action<int> action) { }
                async Task M()
                {
                    await SaveAsync();
                    Run(x => Validate(x));
                }
            }
            """, new DiscardResultCodeFix());

        Assert.Equal("""
            class C
            {
                Task<Result> SaveAsync() => Task.FromResult(Result.Success());
                Result Validate(int x) => Result.Success();
                void Run(Action<int> action) { }
                async Task M()
                {
                    _ = await SaveAsync();
                    Run(x => _ = Validate(x));
                }
            }
            """, fixedCode.Code);
        Assert.Equal(2, fixedCode.Titles.Count);
    }
}

public sealed class TryAsyncCodeFixTests
{
    [Fact]
    public async Task Inside_an_async_method_the_call_becomes_an_awaited_TryAsync()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                async Task<int> M()
                {
                    var result = Result.Try(async () => await LoadAsync(), ex => Error.Unexpected(ex.Message));
                    return result.Match(v => 0, e => 1);
                }
            }
            """, new TryAsyncCodeFix());

        Assert.Equal("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                async Task<int> M()
                {
                    var result = await Result.TryAsync(async () => await LoadAsync(), ex => Error.Unexpected(ex.Message));
                    return result.Match(v => 0, e => 1);
                }
            }
            """, fixedCode.Code);
        Assert.Equal([TryAsyncCodeFix.TitleWithAwait], fixedCode.Titles);
    }

    [Fact]
    public async Task A_chained_call_is_parenthesized_after_await()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                async Task<string> M() =>
                    Result.Try(async () => await LoadAsync(), (TimeoutException ex) => Error.Unavailable(ex.Message))
                        .Match(v => "ok", e => e.Code);
            }
            """, new TryAsyncCodeFix());

        Assert.Equal("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                async Task<string> M() =>
                    (await Result.TryAsync(async () => await LoadAsync(), (TimeoutException ex) => Error.Unavailable(ex.Message)))
                        .Match(v => "ok", e => e.Code);
            }
            """, fixedCode.Code);
    }

    [Fact]
    public async Task Outside_an_async_function_only_the_name_changes()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                Task<Result<int>> M()
                {
                    var pending = Result.Try(async () => await LoadAsync(), ex => Error.Unexpected(ex.Message));
                    return Task.FromResult(Result<int>.Success(1));
                }
            }
            """, new TryAsyncCodeFix());

        Assert.Contains("var pending = Result.TryAsync(async () => await LoadAsync()", fixedCode.Code);
        Assert.Equal([TryAsyncCodeFix.Title], fixedCode.Titles);
    }
}

public sealed class BindCodeFixTests
{
    [Fact]
    public async Task Map_becomes_Bind()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                Result<int> M(Result<string> input) => input.Map(s => Parse(s));
            }
            """, new BindCodeFix());

        Assert.Equal("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                Result<int> M(Result<string> input) => input.Bind(s => Parse(s));
            }
            """, fixedCode.Code);
        Assert.Equal(["Use Bind"], fixedCode.Titles);
    }

    [Fact]
    public async Task MapAsync_becomes_BindAsync_and_explicit_type_arguments_go()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Task<Result<int>> LoadAsync(string s) => Task.FromResult(Result<int>.Success(1));
                Result<int> Parse(string s) => Result<int>.Success(1);
                Task<Result<int>> M(Result<string> input) => input.MapAsync(s => LoadAsync(s));
                Result<int> N(Result<string> input) => input.Map<Result<int>>(s => Parse(s));
            }
            """, new BindCodeFix());

        Assert.Equal("""
            class C
            {
                Task<Result<int>> LoadAsync(string s) => Task.FromResult(Result<int>.Success(1));
                Result<int> Parse(string s) => Result<int>.Success(1);
                Task<Result<int>> M(Result<string> input) => input.BindAsync(s => LoadAsync(s));
                Result<int> N(Result<string> input) => input.Bind(s => Parse(s));
            }
            """, fixedCode.Code);
        Assert.Equal(["Use BindAsync", "Use Bind"], fixedCode.Titles);
    }

    [Fact]
    public async Task Map_on_a_tuple_becomes_Bind()
    {
        var fixedCode = await FixAsync("""
            class C
            {
                Result<string> Create(int a, int b) => Result<string>.Success("x");
                Result<string> M(Result<int> a, Result<int> b) => Result.CombineAll(a, b).Map((x, y) => Create(x, y));
            }
            """, new BindCodeFix());

        Assert.Contains("Result.CombineAll(a, b).Bind((x, y) => Create(x, y))", fixedCode.Code);
    }
}
