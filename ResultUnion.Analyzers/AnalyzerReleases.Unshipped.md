; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RU0005 | Usage | Warning | Cancellation turned into a failure
RU0006 | Usage | Info | Error created outside an error catalog
RU0007 | Usage | Info | Field set where the field name is not known
RU0008 | Usage | Info | ValueTask of a Result
