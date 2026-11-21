---
tags: [skill-creator]
max_turns: 30
timeout_seconds: 900
allowed_tools: [Read, Glob, Grep, Skill, Write, Edit, Bash]
---

Our CRM backend uses the ResultUnion library (Result<T>, Error, ErrorType) everywhere. I need two value objects in Domain/: PhoneNumber — Georgian mobile numbers only, like +995 555 12 34 56; people type them with spaces or dashes and sometimes without the +995, normalize to +9955XXXXXXXX; and CustomerName — required, trimmed, at most 100 characters. Invalid input must not throw. Please write Domain/PhoneNumber.cs and Domain/CustomerName.cs.
