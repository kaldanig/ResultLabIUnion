; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RU0001 | Usage | Warning | A Result is discarded
RU0002 | Usage | Warning | Asynchronous code passed to Result.Try
RU0003 | Usage | Warning | Map with a mapper that returns a Result
RU0004 | Usage | Warning | ResultValue or Error read without checking the result

## Release 0.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RU0005 | Usage | Warning | Cancellation turned into a failure
RU0006 | Usage | Info | Error created outside an error catalog
RU0007 | Usage | Info | Field set where the field name is not known
RU0008 | Usage | Info | ValueTask of a Result
RU0009 | Usage | Warning | Result<Error>, an error as a success value
