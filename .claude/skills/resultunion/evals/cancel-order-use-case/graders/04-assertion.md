---
type: llm
focus: trace
weight: 1
---

A missing order becomes a NotFound error through Result.FromNullable or ToResultAsync (or an equivalent single-step conversion), not an if (order is null) branch in the handler
