---
tags: [skill-creator]
max_turns: 30
timeout_seconds: 900
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

In our app (ResultUnion for results, ResultUnion.AspNetCore for Minimal APIs) add a "create customer" feature. Input: name, phone, email (all strings from the client). Validate all three at once so the UI can show every bad field, reject a phone number that's already used by another customer, save the customer, and expose POST /customers that returns 201 with the new customer. Assume we already have value objects CustomerName, PhoneNumber and Email with static Create(string?) methods returning Result of themselves, and an ICustomerRepository with Task<Customer?> FindByPhoneAsync(PhoneNumber phone, CancellationToken ct) and Task AddAsync(Customer customer, CancellationToken ct). Write Application/CreateCustomer.cs (command + handler) and Api/CustomerEndpoints.cs.
