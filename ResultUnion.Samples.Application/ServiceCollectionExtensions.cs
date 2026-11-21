using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResultUnion.Samples.Application.Infrastructure;
using ResultUnion.Samples.Domain;

namespace ResultUnion.Samples.Application;

/// <summary>Registration shared by the hosts (Web API and gRPC service).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the sample use cases with in-memory storage.</summary>
    public static IServiceCollection AddSampleApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryUserStore>();
        services.AddSingleton<InMemoryOrderStore>();
        services.AddSingleton<PasswordResetQuota>();
        services.AddScoped<UserService>();
        services.AddScoped<OrderService>();

        return services;
    }

    /// <summary>Registers ann@example.com, so there is a user to look up, deactivate and order for.</summary>
    public static IServiceProvider SeedSampleData(this IServiceProvider services)
    {
#pragma warning disable RU0004 // seed data: the address is a constant that is valid
        services.GetRequiredService<InMemoryUserStore>()
            .Seed(new User(Guid.NewGuid(), Email.Create("ann@example.com").ResultValue, 34));
#pragma warning restore RU0004

        return services;
    }
}
