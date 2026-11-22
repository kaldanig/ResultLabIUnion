#if (Crud)
using Google.Protobuf.WellKnownTypes;
#endif
using Grpc.Core;
using ResultUnion;
using ResultUnion.Grpc;
using TargetNamespace.Gizmos.V1;

namespace TargetNamespace.Gizmos;

/// <summary>
/// The gRPC host of the use cases. Each method maps the request to a command, runs the use case, and ends with
/// <c>GetValueOrThrowRpcAsync</c>: the reply, or an <see cref="RpcException"/> whose status is chosen by the error's type,
/// with an ErrorInfo for the code and arguments and, for validation, a BadRequest with the invalid field.
/// </summary>
public sealed class WidgetCatalogService(WidgetService service) : WidgetCatalog.WidgetCatalogBase
{
    /// <summary>ErrorInfo.domain: who defines the codes.</summary>
    private const string Domain = "TargetNamespace";

    public override Task<WidgetReply> CreateWidget(CreateWidgetRequest request, ServerCallContext context) =>
        service.CreateAsync(new CreateWidget(request.Name), context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    // Protobuf has no GUID, so the id arrives as text; HTTP checks it in the route, gRPC here.
    public override Task<WidgetReply> GetWidget(GetWidgetRequest request, ServerCallContext context) =>
        Widget.ParseId(request.Id)
            .MapError(e => e.WithField("id"))
            .BindAsync(id => service.FindAsync(id, context.CancellationToken))
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);
#if (Crud)

    public override Task<WidgetReply> RenameWidget(RenameWidgetRequest request, ServerCallContext context) =>
        Widget.ParseId(request.Id)
            .MapError(e => e.WithField("id"))
            .BindAsync(id => service.RenameAsync(new RenameWidget(id, request.Name, request.Version), context.CancellationToken))
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    public override Task<Empty> RemoveWidget(RemoveWidgetRequest request, ServerCallContext context) =>
        Widget.ParseId(request.Id)
            .MapError(e => e.WithField("id"))
            .BindAsync(id => service.RemoveAsync(id, request.Version, context.CancellationToken))
            .ToReplyOrThrowRpcAsync(() => new Empty(), Domain);

    /// <summary>What a client receives. A <c>Result</c> is never sent; its value is mapped to the reply.</summary>
    private static WidgetReply ToReply(Widget entity) => new() { Id = entity.Id.ToString(), Name = entity.Name.Value, Version = entity.Version };
#else

    /// <summary>What a client receives. A <c>Result</c> is never sent; its value is mapped to the reply.</summary>
    private static WidgetReply ToReply(Widget entity) => new() { Id = entity.Id.ToString(), Name = entity.Name.Value };
#endif
}
