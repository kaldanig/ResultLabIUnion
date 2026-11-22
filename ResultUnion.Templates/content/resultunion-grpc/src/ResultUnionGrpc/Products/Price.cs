namespace ResultUnionGrpc.Products;

/// <summary>The price of a product: required and greater than zero.</summary>
public sealed record Price
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("price.required", "The price is required.", ErrorType.Validation);

        public static Error NotPositive(decimal amount) =>
            Error.Create("price.not_positive", $"The price must be greater than zero; {amount} is not.", ErrorType.Validation)
                .WithArgument("amount", amount);
    }

    public decimal Amount { get; }

    private Price(decimal amount) => Amount = amount;

    public static Result<Price> Create(decimal? amount) => amount switch
    {
        null => Errors.Required,
        > 0 => new Price(amount.Value),
        _ => Errors.NotPositive(amount.Value),
    };

    public override string ToString() => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
