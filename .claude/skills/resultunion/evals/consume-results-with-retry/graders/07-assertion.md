---
type: llm
focus: trace
weight: 1
---

Values and errors of a Result are read with a switch on the union, Match, TryGetResultValue or TryGetError; never the result's own TryGetValue(out var ...), and never ResultValue or Error after a separate IsSuccess check (a dictionary TryGetValue on error.Arguments is fine)
