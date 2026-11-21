---
tags: [skill-creator]
max_turns: 40
timeout_seconds: 1200
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

The OrdersApp project (files/OrdersApp) is a Minimal API that references the ResultUnion and ResultUnion.AspNetCore NuGet packages. Add a "cancel order" use case: POST /orders/{id}/cancel with a JSON body { "reason": "..." }. Rules: the order must exist; only a Pending order can be cancelled (a Paid or Shipped one cannot, and cancelling twice is not allowed either); the reason is required and at most 200 characters. IOrderRepository.SaveChangesAsync can throw OrderConcurrencyException when someone else changed the order; the client should be told to reload and retry, any other exception is a bug. The endpoint returns 204 on success. Put the rule in the domain, the use case in an application handler, wire the endpoint in Program.cs, and make sure dotnet build succeeds.
