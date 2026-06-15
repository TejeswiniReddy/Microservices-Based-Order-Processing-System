using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;
using OrderFlow.Contracts.Models;

namespace OrderFlow.OrderService.Handlers;

/// <summary>
/// These handlers advance the order's state machine in response to events
/// published by the Payment and Inventory services. Together with those services
/// they form a choreography-based saga: no central coordinator, each service
/// reacts to events and emits the next one (including compensation).
/// </summary>
public sealed class PaymentCompletedHandler : IIntegrationEventHandler<PaymentCompleted>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<PaymentCompletedHandler> _logger;

    public PaymentCompletedHandler(IOrderRepository orders, ILogger<PaymentCompletedHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentCompleted e, CancellationToken ct = default)
    {
        var order = await _orders.GetAsync(e.OrderId, ct);
        if (order is null) return;

        order.Status = OrderStatus.PaymentCompleted;
        order.PaymentId = e.PaymentId;
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} payment completed ({PaymentId})", e.OrderId, e.PaymentId);
    }
}

public sealed class PaymentFailedHandler : IIntegrationEventHandler<PaymentFailed>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<PaymentFailedHandler> _logger;

    public PaymentFailedHandler(IOrderRepository orders, ILogger<PaymentFailedHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentFailed e, CancellationToken ct = default)
    {
        var order = await _orders.GetAsync(e.OrderId, ct);
        if (order is null) return;

        order.Status = OrderStatus.Cancelled;
        order.StatusReason = $"Payment failed: {e.Reason}";
        await _orders.UpdateAsync(order, ct);
        _logger.LogWarning("Order {OrderId} cancelled: {Reason}", e.OrderId, e.Reason);
    }
}

public sealed class InventoryReservedHandler : IIntegrationEventHandler<InventoryReserved>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<InventoryReservedHandler> _logger;

    public InventoryReservedHandler(IOrderRepository orders, ILogger<InventoryReservedHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(InventoryReserved e, CancellationToken ct = default)
    {
        var order = await _orders.GetAsync(e.OrderId, ct);
        if (order is null) return;

        order.Status = OrderStatus.Confirmed;
        order.ReservationId = e.ReservationId;
        order.StatusReason = null;
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} confirmed (reservation {ReservationId})", e.OrderId, e.ReservationId);
    }
}

public sealed class InventoryOutOfStockHandler : IIntegrationEventHandler<InventoryOutOfStock>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<InventoryOutOfStockHandler> _logger;

    public InventoryOutOfStockHandler(IOrderRepository orders, ILogger<InventoryOutOfStockHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(InventoryOutOfStock e, CancellationToken ct = default)
    {
        var order = await _orders.GetAsync(e.OrderId, ct);
        if (order is null) return;

        // Payment is refunded by the Payment service's compensating handler.
        order.Status = OrderStatus.Cancelled;
        order.StatusReason = $"Out of stock: {e.Sku}";
        await _orders.UpdateAsync(order, ct);
        _logger.LogWarning("Order {OrderId} cancelled - out of stock: {Sku}", e.OrderId, e.Sku);
    }
}
