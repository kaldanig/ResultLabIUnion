using Grpc.Core;
using ResultUnion.Grpc;
using ResultUnion.GrpcDemo;
using ResultUnion.Samples.Application;
using ResultUnion.Samples.Domain;
using OrderLine = ResultUnion.GrpcDemo.OrderLine;

namespace ResultUnion.GrpcService.Services;

public sealed class OrdersGrpcService(OrderService orders) : Orders.OrdersBase
{
    public override Task<OrderReply> PlaceOrder(PlaceOrderRequest request, ServerCallContext context) =>
        orders.PlaceAsync(
                new PlaceOrder(request.CustomerEmail, [.. request.Lines.Select(line => new OrderLineInput(line.Product, line.Quantity))]),
                context.CancellationToken)
            .MapAsync(order => new OrderReply
            {
                Id = order.Id.ToString(),
                Customer = order.Customer.Value,
                Lines = { order.Lines.Select(line => new OrderLine { Product = line.Product, Quantity = line.Quantity }) },
                TotalQuantity = order.TotalQuantity,
            })
            .GetValueOrThrowRpcAsync(UsersGrpcService.Domain);
}
