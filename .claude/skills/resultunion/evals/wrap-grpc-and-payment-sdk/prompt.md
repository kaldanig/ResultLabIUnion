---
tags: [skill-creator]
max_turns: 30
timeout_seconds: 900
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

Our OrdersService (it uses ResultUnion and ResultUnion.Grpc) needs two adapters in Infrastructure/StockAndPayments.cs: (1) ReserveStockAsync(string sku, int quantity, CancellationToken ct) that calls the inventory gRPC service through the generated client Inventory.InventoryClient (method ReserveAsync(ReserveRequest) returning ReserveReply) and returns a result instead of throwing when the inventory service says no; (2) ChargeAsync(decimal amount, CancellationToken ct) around the vendor SDK PaymentGateway.ChargeAsync(decimal amount, CancellationToken ct) which returns a transaction id string and throws PaymentDeclinedException when the card is declined — a declined card is a normal business outcome for us, but any other exception from the SDK means an outage and should not be swallowed.
