namespace ResultUnion.Showcase.Domain;

public sealed record User(Guid Id, Email Email, int Age)
{
    public const int MinimumAge = 18;

    /// <summary>Every way a user operation can be rejected. Codes follow <c>&lt;concept&gt;.&lt;case&gt;</c>.</summary>
    public static class Errors
    {
        public static Error AgeNotANumber(string? input) =>
            Error.Create("user.age_not_a_number", $"'{input}' is not a number.", ErrorType.Validation);

        public static Error AgeOutOfRange(int age) =>
            Error.Create("user.age_out_of_range", $"Age {age} is out of range.", ErrorType.Validation);

        public static Error TooYoung(User user) =>
            Error.Create("user.too_young", $"Users must be at least {MinimumAge}; {user.Email} is {user.Age}.", ErrorType.BusinessRule);

        public static Error EmailAlreadyRegistered(Email email) =>
            Error.Create("user.email_already_registered", $"Email {email} is already registered.", ErrorType.Conflict);

        public static Error NotFound(Email email) =>
            Error.Create("user.not_found", $"No user with email {email}.", ErrorType.NotFound);

        public static Error AlreadyInactive(User user) =>
            Error.Create("user.already_inactive", $"User {user.Email} is already inactive.", ErrorType.Conflict);
    }

    public bool IsActive { get; private set; } = true;

    public bool IsAdult => Age >= MinimumAge;

    public static Result<int> ParseAge(string? input) =>
        int.TryParse(input, out var age)
            ? Result.Success(age).Ensure(a => a is >= 0 and <= 150, Errors.AgeOutOfRange)
            : Errors.AgeNotANumber(input);

    /// <summary>A command without a return value: <see cref="Result"/> instead of <see langword="void"/> plus exceptions.</summary>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Errors.AlreadyInactive(this);
        }

        IsActive = false;

        return Result.Success();
    }
}
