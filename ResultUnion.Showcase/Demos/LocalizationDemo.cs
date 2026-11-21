using ResultUnion.Showcase.Application;
using ResultUnion.Showcase.Domain;

namespace ResultUnion.Showcase.Demos;

/// <summary>Error arguments: the values behind a message, so it can be translated by code without parsing the English text.</summary>
internal static class LocalizationDemo
{
    public static void Run()
    {
        var service = new RegistrationService(new UserRepository());
        service.Register("ann@example.com", "34");

        Error[] errors =
        [
            service.Register("bob@example.com", "15").Error,                        // user.too_young
            service.Register("ann@example.com", "40").Error,                        // user.email_already_registered
            User.ParseAge("200").Error,                                             // user.age_out_of_range
            .. Result.CombineAll(Email.Create("dan@"), Password.Create("123")).Error.Flatten(),
            .. new[] { new OrderLineInput("", 0) }.CombineAll(OrderLine.Create, "lines").Error.Flatten(),
        ];

        foreach (var error in errors)
        {
            var arguments = error.Arguments.Count == 0
                ? "-"
                : string.Join(", ", error.Arguments.Select(pair => $"{pair.Key}={pair.Value}"));

            Console.WriteLine($"  {error.Code}  ({arguments})");
            Demo.Show("    en (Message)", error.Message);
            Demo.Show("    ka (code + arguments)", ErrorLocalizer.ToGeorgian(error));
        }
    }
}
