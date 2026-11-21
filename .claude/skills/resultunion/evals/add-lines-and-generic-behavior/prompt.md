---
tags: [skill-creator]
max_turns: 40
timeout_seconds: 1200
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

The OrdersApp project (files/OrdersApp) is a Minimal API that references the ResultUnion and ResultUnion.AspNetCore NuGet packages. Two things: (1) an "add lines" use case: POST /orders/{id}/lines with { "lines": [ { "sku": "...", "quantity": 1 }, ... ] }. A sku is required and looks like ABC-123 (three uppercase letters, a dash, digits), quantity is 1..99, at least one line and at most 20; the client form needs every invalid line reported at once with its position so it can mark the right row; 404 if the order does not exist. (2) We call handlers from several hosts, so write a reusable decorator/behavior class that logs every failed handler call with the error code and short-circuits with a Forbidden error when the caller is not allowed. It must work for handlers returning Result as well as Result<T> of any T, without reflection. Wire the endpoint in Program.cs and make sure dotnet build succeeds.
