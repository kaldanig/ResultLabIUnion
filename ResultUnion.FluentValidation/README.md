# ResultUnion.FluentValidation

FluentValidation integration for [ResultUnion](https://www.nuget.org/packages/ResultUnion): a validator's outcome
becomes a `Result`, with one field error per failure, so it composes with the rest of a pipeline and reaches HTTP or
gRPC clients as field errors.

```csharp
using ResultUnion.FluentValidation;

public sealed class RegisterUserValidator : AbstractValidator<RegisterUser>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithErrorCode("email.required");
        RuleFor(x => x.Password).MinimumLength(8).WithErrorCode("password.too_short");
    }
}

// The command itself on success, or every failure at once.
Task<Result<RegisterUser>> valid = validator.ValidateToResultAsync(command, ct);

// Or from a ValidationResult you already have.
Result result = validator.Validate(command).ToResult();
```

Each failure becomes a `Validation` error: the rule's error code (set stable ones with `WithErrorCode`), its message,
the property path in camel case as the field (`Lines[1].Quantity` → `lines[1].quantity`; pass your own naming to
`ToResult(fieldName)`), and the rule's number, string and Boolean placeholders (such as `minLength`) as arguments. The
attempted value is never copied. One failure comes back as it is; several are grouped, as `Result.CombineAll` does.

- Documentation: <https://kaldanig.github.io/ResultLabIUnion/>
- Source: <https://github.com/kaldanig/ResultLabIUnion>
