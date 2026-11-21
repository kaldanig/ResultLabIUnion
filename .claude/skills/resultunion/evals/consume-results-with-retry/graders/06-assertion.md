---
type: llm
focus: trace
weight: 1
---

The retrier decides what to retry with error.IsTransient (or by ErrorType Unavailable/TooManyRequests), reads the retry-after value from the error arguments, and passes ct to Task.Delay
