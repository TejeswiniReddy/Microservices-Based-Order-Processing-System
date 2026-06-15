using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.InventoryService.Handlers;

/// <summary>
/// The reservation step runs on <see cref="PaymentCompleted"/>, which only
/// carries ids. To stay self-contained, the Inventory service captures the
/// order's line items the moment the order is created (OrderCreated carries
/// them) and stashes them in the shared cache. When payment later completes,
/// <see cref="ReserveOnPaymentCompletedHandler"/> reads them back. In a real
/// deployment this cache is Redis, so any Inventory replica can serve the read.
/// </summary>
public sealed class CacheItemsOnOrderCreatedHandler : IIntegrationEventHandler<OrderCreated>
{
    private readonly ICacheService _cache;
    private readonly ILogger<CacheItemsOnOrderCreatedHandler> _logger;

    public CacheItemsOnOrderCreatedHandler(
        ICacheService cache,
        ILogger<CacheItemsOnOrderCreatedHandler> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreated e, CancellationToken ct = default)
    {
        await _cache.SetAsync(CacheKeys.OrderItems(e.OrderId), e.Items, TimeSpan.FromHours(1), ct);
        _logger.LogInformation("Cached {Count} line item(s) for order {OrderId}", e.Items.Count, e.OrderId);
    }
}
