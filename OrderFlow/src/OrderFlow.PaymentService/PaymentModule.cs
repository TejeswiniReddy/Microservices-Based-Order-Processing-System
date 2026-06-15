using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;
using OrderFlow.PaymentService.Handlers;
using OrderFlow.PaymentService.Persistence;
using OrderFlow.Platform.Messaging;

namespace OrderFlow.PaymentService;

public static class PaymentModule
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        services.AddEventHandler<OrderCreated, ChargeOnOrderCreatedHandler>();
        services.AddEventHandler<InventoryOutOfStock, RefundOnOutOfStockHandler>();
        return services;
    }
}
