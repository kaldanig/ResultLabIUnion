---
type: llm
focus: trace
weight: 1
---

503 and a timeout (a TaskCanceledException not caused by ct, or HttpRequestException) become ErrorType.Unavailable errors; caller cancellation propagates and is not turned into an error
