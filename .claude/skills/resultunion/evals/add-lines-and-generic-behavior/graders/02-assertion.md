---
type: llm
focus: trace
weight: 1
---

Line validation returns Result values with catalog errors (specific codes such as order_line.sku_required, order_line.invalid_sku, order_line.quantity_out_of_range) carrying limits or values via WithArgument, and never throws for bad input
