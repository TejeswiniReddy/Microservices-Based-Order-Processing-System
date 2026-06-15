using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.InventoryService.Handlers;

/// <summary>
/// After payment succeeds, attempt to reserve stock. Success -> InventoryReserved
/// (order is confirmed). Failure -> InventoryOutOfStock (triggers payment refund
/// compensation and order cancellation).
/// </summary>
public sealed class ReserveOnPaymentCompletedHandler : IIntegrationEventHandler<PaymentCompleted>
{
    private readonly IInventoryRepository _inventory;
    private readonly IEventBus _bus;
    private readonly ICacheService _cache;
    private readonly ILogger<ReserveOnPaymentCompletedHandler> _logger;

    // We need the order's line items to reserve. In this choreography the
    // PaymentCompleted event carries only ids, so we look the order up via the
    // cache the OrderCreated flow populated. To keep the services decoupled and
    // self-contained, the Order service stamps items into the shared cache keyed
    // by order id when the order is created.
    public ReserveOnPaymentCompletedHandler(
        IInventoryRepository inventory,
        IEventBus bus,
        ICacheService cache,
        ILogger<ReserveOnPaymentCompletedHandler> logger)
    {
        _inventory = inventory;
        _bus = bus;
        _cache = cache;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentCompleted e, CancellationToken ct = default)
    {
        var items = await _cache.GetAsync<List<Contracts.Models.OrderItem>>(CacheKeys.OrderItems(e.OrderId), ct);
        if (items is null || items.Count == 0)
        {
            // Nothing to reserve we can see; treat as out of stock to be safe.
            await _bus.PublishAsync(new InventoryOutOfStock { OrderId = e.OrderId, Sku = "unknown" }, ct);
            return;
        }

        if (await _inventory.TryReserveAsync(items, ct))
        {
            await _bus.PublishAsync(new InventoryReserved { OrderId = e.OrderId, ReservationId = Guid.NewGuid() }, ct);
            _logger.LogInformation("Reserved stock for order {OrderId}", e.OrderId);
        }
        else
        {
            var firstShort = items[0].Sku;
            await _bus.PublishAsync(new InventoryOutOfStock { OrderId = e.OrderId, Sku = firstShort }, ct);
            _logger.LogWarning("Out of stock for order {OrderId}", e.OrderId);
        }
    }
}
