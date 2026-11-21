using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class DiscardedResultTests
{
    [Fact]
    public async Task Discarded_result_of_T_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M() { Parse("1"); }
            }
            """);

        Assert.Equal([("RU0001", 4)], diagnostics);
    }

    [Fact]
    public async Task Discarded_result_without_value_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result Validate() => Result.Success();
                void M() { Validate(); }
            }
            """);

        Assert.Equal([("RU0001", 4)], diagnostics);
    }

    [Fact]
    public async Task Awaited_result_that_is_discarded_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<Result> SaveAsync() => Task.FromResult(Result.Success());
                async Task M() { await SaveAsync(); }
            }
            """);

        Assert.Equal([("RU0001", 4)], diagnostics);
    }

    [Fact]
    public async Task Task_of_result_that_is_neither_awaited_nor_used_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<Result<int>> LoadAsync() => Task.FromResult(Result<int>.Success(1));
                void M() { LoadAsync(); }
            }
            """);

        Assert.Equal([("RU0001", 4)], diagnostics);
    }

    [Fact]
    public async Task Result_discarded_inside_a_void_lambda_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result Validate(int x) => Result.Success();
                void M(Action<int> run) { }
                void N() { M(x => Validate(x)); }
            }
            """);

        Assert.Equal([("RU0001", 5)], diagnostics);
    }

    [Fact]
    public async Task Used_results_and_explicit_discards_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                Task<Result> SaveAsync() => Task.FromResult(Result.Success());

                Result<int> Returned() => Parse("1");
                async Task<Result> Awaited() => await SaveAsync();

                async Task M()
                {
                    var parsed = Parse("1");
                    _ = Parse("2");
                    _ = await SaveAsync();
                    var text = Parse("3").Match(v => v.ToString(), e => e.Code);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Tap_on_a_variable_is_not_reported_but_tap_on_a_call_is()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M(Result<int> parameter)
                {
                    var local = Parse("1");
                    local.TapError(e => Console.WriteLine(e.Code));
                    parameter.Tap(v => Console.WriteLine(v));
                    Parse("2").TapError(e => Console.WriteLine(e.Code));
                }
            }
            """);

        Assert.Equal([("RU0001", 9)], diagnostics);
    }

    [Fact]
    public async Task Non_result_calls_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                int Count() => 1;
                Task Work() => Task.CompletedTask;
                async Task M() { Count(); await Work(); }
            }
            """);

        Assert.Empty(diagnostics);
    }
}

public sealed class AsyncInTryTests
{
    [Fact]
    public async Task Async_lambda_returning_a_value_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                void M()
                {
                    var r = Result.Try(async () => await LoadAsync(), ex => Error.Unexpected(ex.Message));
                }
            }
            """);

        Assert.Equal([("RU0002", 6)], diagnostics);
    }

    [Fact]
    public async Task Async_void_lambda_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                void M()
                {
                    var r = Result.Try(async () => { await Task.Delay(1); }, ex => Error.Unexpected(ex.Message));
                }
            }
            """);

        Assert.Equal([("RU0002", 5)], diagnostics);
    }

    [Fact]
    public async Task Method_group_returning_a_task_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                void M()
                {
                    var r = Result.Try(LoadAsync, ex => Error.Unexpected(ex.Message));
                }
            }
            """);

        Assert.Equal([("RU0002", 6)], diagnostics);
    }

    [Fact]
    public async Task Typed_try_with_an_async_lambda_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                void M()
                {
                    var r = Result.Try(async () => await LoadAsync(), (TimeoutException ex) => Error.Unavailable(ex.Message));
                }
            }
            """);

        Assert.Equal([("RU0002", 6)], diagnostics);
    }

    [Fact]
    public async Task Synchronous_try_and_try_async_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<int> LoadAsync() => Task.FromResult(1);
                async Task M()
                {
                    var a = Result.Try(() => int.Parse("1"), ex => Error.Validation(ex.Message));
                    var b = Result.Try(() => Console.WriteLine(), ex => Error.Unexpected(ex.Message));
                    var c = await Result.TryAsync(async () => await LoadAsync(), ex => Error.Unexpected(ex.Message));
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}

public sealed class MapReturnsResultTests
{
    [Fact]
    public async Task Map_with_a_mapper_returning_a_result_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M(Result<string> input)
                {
                    var nested = input.Map(s => Parse(s));
                }
            }
            """);

        Assert.Equal([("RU0003", 6)], diagnostics);
    }

    [Fact]
    public async Task Map_with_a_mapper_returning_a_result_without_value_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result Check(string s) => Result.Success();
                void M(Result<string> input)
                {
                    var nested = input.Map(Check);
                }
            }
            """);

        Assert.Equal([("RU0003", 6)], diagnostics);
    }

    [Fact]
    public async Task MapAsync_with_an_async_mapper_returning_a_result_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<Result<int>> LoadAsync(string s) => Task.FromResult(Result<int>.Success(1));
                async Task M(Result<string> input)
                {
                    var nested = await input.MapAsync(s => LoadAsync(s));
                }
            }
            """);

        Assert.Equal([("RU0003", 6)], diagnostics);
    }

    [Fact]
    public async Task MapAsync_on_a_task_with_a_mapper_returning_a_result_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Task<Result<string>> ReadAsync() => Task.FromResult(Result<string>.Success("1"));
                Result<int> Parse(string s) => Result<int>.Success(1);
                async Task M()
                {
                    var nested = await ReadAsync().MapAsync(s => Parse(s));
                }
            }
            """);

        Assert.Equal([("RU0003", 7)], diagnostics);
    }

    [Fact]
    public async Task Map_on_a_tuple_with_a_factory_returning_a_result_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<string> Create(int a, int b) => Result<string>.Success("x");
                void M(Result<int> a, Result<int> b)
                {
                    var nested = Result.CombineAll(a, b).Map((x, y) => Create(x, y));
                }
            }
            """);

        Assert.Equal([("RU0003", 6)], diagnostics);
    }

    [Fact]
    public async Task Map_with_a_plain_value_and_bind_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                Result<int> Parse(string s) => Result<int>.Success(1);
                void M(Result<string> input)
                {
                    var length = input.Map(s => s.Length);
                    var parsed = input.Bind(s => Parse(s));
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
