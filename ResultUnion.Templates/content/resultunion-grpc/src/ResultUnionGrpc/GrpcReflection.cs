using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Reflection;
using ServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;   // not the DI one

namespace ResultUnionGrpc;

/// <summary>
/// gRPC reflection that also describes error_details.proto, so grpcurl, Postman and others show a failure's ErrorInfo
/// and BadRequest as JSON instead of bytes. By default reflection describes only the services' own messages.
/// </summary>
public static class GrpcReflection
{
    /// <summary>
    /// Adds reflection for every service mapped with <c>MapGrpcService</c>, found when a client first asks, as the
    /// default reflection does; map it with <c>MapGrpcReflectionService</c>.
    /// </summary>
    public static IServiceCollection AddGrpcReflectionWithErrorDetails(this IServiceCollection services)
    {
        services.AddGrpcReflection();

        // Registered after AddGrpcReflection, so these replace its two implementations (reflection v1 and v1alpha).
        services.AddSingleton(provider => Describe(provider, (names, symbols) => new ReflectionServiceImpl(names, symbols)));
        services.AddSingleton(provider => Describe(provider, (names, symbols) => new ReflectionV1ServiceImpl(names, symbols)));
        return services;
    }

    private static T Describe<T>(IServiceProvider provider, Func<IEnumerable<string>, SymbolRegistry, T> create)
    {
        var mapped = provider.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>()?.ServiceType)
            .OfType<Type>()
            .Distinct()
            .Select(FindDescriptor)
            .OfType<ServiceDescriptor>()
            .ToList();

        var files = mapped.Select(service => service.File).Append(Google.Rpc.ErrorDetailsReflection.Descriptor);
        return create(mapped.Select(service => service.FullName), SymbolRegistry.FromFiles(files));
    }

    // A service derives from a generated base class whose BindServiceMethod attribute names the generated class that
    // has the service's descriptor.
    private static ServiceDescriptor? FindDescriptor(Type serviceType)
    {
        for (var type = serviceType; type is not null; type = type.BaseType)
        {
            if (type.GetCustomAttribute<BindServiceMethodAttribute>() is { } binding)
                return binding.BindType.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ServiceDescriptor;
        }

        return null;
    }
}
