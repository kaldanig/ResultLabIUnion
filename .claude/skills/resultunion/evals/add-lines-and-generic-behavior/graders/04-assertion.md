---
type: llm
focus: trace
weight: 1
---

The list is validated with the collection form of CombineAll (for example request.Lines.CombineAll(OrderLine.Create, field: "lines")) so every invalid line is reported with an indexed field like lines[1].quantity, not with a hand-written loop that builds field names or stops at the first error
