using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;
using OrderFlow.InventoryService.Handlers;
using OrderFlow.InventoryService.Persistence;
using OrderFlow.Platform.Messaging;

namespace OrderFlow.InventoryService;

public static class InventoryModule
{
    /// <summary>
    /// Registers the Inventory service's catalog store and saga handlers:
    /// it captures line items on OrderCreated and reserves stock on
    /// PaymentCompleted.
    /// </summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IInventoryRepository, InMemoryInventoryRepository>();

        services.AddEventHandler<OrderCreated, CacheItemsOnOrderCreatedHandler>();
        services.AddEventHandler<PaymentCompleted, ReserveOnPaymentCompletedHandler>();

        return services;
    }
}
