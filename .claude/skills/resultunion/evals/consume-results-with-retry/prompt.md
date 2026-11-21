---
tags: [skill-creator]
max_turns: 40
timeout_seconds: 1200
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

The OrdersApp project (files/OrdersApp) references the ResultUnion NuGet package. Write two pieces in Application/: (1) PaymentClient.ChargeAsync(Guid orderId, decimal amount, CancellationToken ct) returning a result with the transaction id. It calls an HTTP payment provider through HttpClient (POST /charges): 2xx gives { "transactionId": "..." }; 402 means the card was declined (a normal outcome the user should see); 429 means rate limited and carries a Retry-After header in seconds that our callers need; 503 or a timeout means the provider is down; anything else is a bug. (2) PaymentRetrier.ChargeWithRetryAsync, which calls ChargeAsync up to 3 times, retrying only failures worth retrying (waiting Retry-After when given, otherwise exponential backoff), never retrying a declined card, and returns the final result. Also a small OrderPaymentReport.Describe(result) that turns the final result into a one-line string for logs: the transaction id, or the error code and message. Make sure dotnet build succeeds.
