using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace ResultUnion.AspNetCore;

/// <summary>OpenAPI metadata for the problem responses an endpoint can return.</summary>
/// <remarks>
/// A typed <c>Results&lt;Ok&lt;T&gt;, ProblemHttpResult&gt;</c> documents the success response, but not the problems: their
/// status depends on the <see cref="ErrorType"/> at run time. Declare the categories an endpoint can fail with, and each one
/// is documented with the status code that <see cref="ProblemDetailsMapping.ToHttpStatusCode(ErrorType)"/> maps it to.
/// </remarks>
public static class EndpointProblemMetadataExtensions
{
    /// <summary>Declares the problem responses for <paramref name="types"/>, one per distinct status code.</summary>
    /// <typeparam name="TBuilder">The endpoint builder, e.g. the <c>RouteHandlerBuilder</c> returned by <c>MapPost</c>, or a route group.</typeparam>
    /// <param name="builder">The endpoint or group to annotate.</param>
    /// <param name="types">The error categories the endpoint can fail with.</param>
    /// <remarks>
    /// <see cref="ErrorType.Validation"/> is documented as an <see cref="HttpValidationProblemDetails"/> (with <c>errors</c>),
    /// every other category as a <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/>, both as <c>application/problem+json</c>.
    /// Categories with the same status (Conflict and Concurrency) are documented once.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="types"/> is <see langword="null"/>.</exception>
    public static TBuilder ProducesErrors<TBuilder>(this TBuilder builder, params ErrorType[] types)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(types);

        var metadata = types
            .GroupBy(type => type.ToHttpStatusCode())
            .Select(group => (IProducesResponseTypeMetadata)new ProducesResponseTypeMetadata(
                group.Key,
                group.Contains(ErrorType.Validation) ? typeof(HttpValidationProblemDetails) : typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
                ["application/problem+json"]))
            .ToArray();

        builder.Add(endpoint =>
        {
            foreach (var item in metadata)
            {
                endpoint.Metadata.Add(item);
            }
        });

        return builder;
    }
}
