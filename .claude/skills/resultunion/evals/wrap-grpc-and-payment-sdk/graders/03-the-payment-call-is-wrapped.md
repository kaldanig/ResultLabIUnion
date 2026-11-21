---
type: llm
focus: trace
weight: 1
---

The payment call is wrapped in Result.TryAsync with a mapper typed as PaymentDeclinedException, so other exceptions and cancellation propagate
