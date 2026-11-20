namespace ResultUnion.Showcase.Domain;

public sealed record User(Guid Id, Email Email, int Age)
{
    public bool IsActive { get; private set; } = true;

    public static Result<int> ParseAge(string? input) =>
        int.TryParse(input, out var age)
            ? Result.Success(age).Ensure(a => a is >= 0 and <= 150, a => Error.Validation($"Age {a} is out of range."))
            : Error.Validation($"'{input}' is not a number.");

    /// <summary>A command without a return value: <see cref="Result"/> instead of <see langword="void"/> plus exceptions.</summary>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Create("user.already_inactive", $"User {Email} is already inactive.", ErrorType.Conflict);
        }

        IsActive = false;

        return Result.Success();
    }
}
