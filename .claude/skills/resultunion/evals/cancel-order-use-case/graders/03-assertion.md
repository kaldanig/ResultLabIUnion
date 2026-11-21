---
type: llm
focus: trace
weight: 1
---

Errors come from a catalog with specific codes (for example order.not_found, order.not_cancellable or order.already_cancelled, a reason code), not inline generic messages in the handler, and the not-found and status errors carry the order id or status with WithArgument
