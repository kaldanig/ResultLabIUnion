using System.Text;
using ResultUnion.Showcase;
using ResultUnion.Showcase.Demos;

Console.OutputEncoding = Encoding.UTF8;                 // section 9 prints Georgian text

Console.WriteLine("ResultUnion showcase: the Result pattern built on C# 15 unions.");

Demo.Section("1. Creating results", BasicsDemo.Run);
Demo.Section("2. Pattern matching on the union", PatternMatchingDemo.Run);
Demo.Section("3. Pipelines: Map, Bind, Ensure, Tap, MapError, Match", PipelineDemo.Run);
Demo.Section("4. Combining independent checks", CombineDemo.Run);
Demo.Section("5. Safe access and common mistakes", SafeAccessDemo.Run);
await Demo.SectionAsync("6. Async pipelines: MapAsync, BindAsync, EnsureAsync, TapAsync, MatchAsync", AsyncDemo.RunAsync);
await Demo.SectionAsync("7. From exceptions to results: Result.Try, Result.TryAsync", TryDemo.RunAsync);
await Demo.SectionAsync("8. HTTP responses with ResultUnion.AspNetCore: ToHttpResult, ProblemDetails", HttpDemo.RunAsync);
Demo.Section("9. Error arguments: translate by code, not by parsing the message", LocalizationDemo.Run);
Demo.Section("10. Errors in messages between services: System.Text.Json", SerializationDemo.Run);
Demo.Section("11. OpenTelemetry: counting failures and tagging spans with Record", TelemetryDemo.Run);

Console.WriteLine();
