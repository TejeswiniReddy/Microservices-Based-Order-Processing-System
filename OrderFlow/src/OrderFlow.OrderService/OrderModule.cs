using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Handlers;
using OrderFlow.OrderService.Persistence;
using OrderFlow.Platform.Messaging;

namespace OrderFlow.OrderService;

public static class OrderModule
{
    /// <summary>
    /// Registers the Order service's persistence and saga handlers. Called by the
    /// service's own Program.cs and by the all-in-one DevHost.
    /// </summary>
    public static IServiceCollection AddOrderModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IOrderRepository, InMemoryOrderRepository>();

        services.AddEventHandler<PaymentCompleted, PaymentCompletedHandler>();
        services.AddEventHandler<PaymentFailed, PaymentFailedHandler>();
        services.AddEventHandler<InventoryReserved, InventoryReservedHandler>();
        services.AddEventHandler<InventoryOutOfStock, InventoryOutOfStockHandler>();

        return services;
    }
}
