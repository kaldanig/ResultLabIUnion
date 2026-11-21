---
type: llm
focus: trace
weight: 1
---

The endpoint returns 204 through ToHttpResultAsync() on a Result (or an explicit TypedResults.NoContent) and declares ProducesErrors with at least NotFound, Conflict (or BusinessRule), Validation and Concurrency
