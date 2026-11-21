---
type: llm
focus: trace
weight: 1
---

The generic behavior is constrained with where TResponse : IResultFactory<TResponse> and short-circuits with TResponse.Failure(error); it uses no reflection, Activator, dynamic or casting to a concrete Result type
