using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.PaymentService.Handlers;

/// <summary>
/// Charges the customer when an order is created, then publishes the next event
/// in the saga. Two deterministic failure rules make the failure path testable:
///   - total amount above the credit limit (5000), or
///   - any line item with SKU "DECLINE".
/// </summary>
public sealed class ChargeOnOrderCreatedHandler : IIntegrationEventHandler<OrderCreated>
{
    private const decimal CreditLimit = 5000m;
    private readonly IPaymentRepository _payments;
    private readonly IEventBus _bus;
    private readonly ILogger<ChargeOnOrderCreatedHandler> _logger;

    public ChargeOnOrderCreatedHandler(
        IPaymentRepository payments, IEventBus bus, ILogger<ChargeOnOrderCreatedHandler> logger)
    {
        _payments = payments;
        _bus = bus;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreated e, CancellationToken ct = default)
    {
        var declined = e.Items.Any(i => string.Equals(i.Sku, "DECLINE", StringComparison.OrdinalIgnoreCase));
        var overLimit = e.TotalAmount > CreditLimit;

        if (declined || overLimit)
        {
            var reason = declined ? "card_declined" : $"over_credit_limit ({e.TotalAmount:C} > {CreditLimit:C})";
            await _bus.PublishAsync(new PaymentFailed { OrderId = e.OrderId, Reason = reason }, ct);
            _logger.LogWarning("Payment declined for order {OrderId}: {Reason}", e.OrderId, reason);
            return;
        }

        var paymentId = await _payments.RecordChargeAsync(e.OrderId, e.TotalAmount, ct);
        await _bus.PublishAsync(new PaymentCompleted
        {
            OrderId = e.OrderId,
            PaymentId = paymentId,
            Amount = e.TotalAmount
        }, ct);
        _logger.LogInformation("Charged {Amount:C} for order {OrderId} (payment {PaymentId})",
            e.TotalAmount, e.OrderId, paymentId);
    }
}

/// <summary>
/// Compensating transaction: if inventory can't be reserved after payment, the
/// charge is refunded. This is the saga's rollback for the payment step.
/// </summary>
public sealed class RefundOnOutOfStockHandler : IIntegrationEventHandler<InventoryOutOfStock>
{
    private readonly IPaymentRepository _payments;
    private readonly IEventBus _bus;
    private readonly ILogger<RefundOnOutOfStockHandler> _logger;

    public RefundOnOutOfStockHandler(
        IPaymentRepository payments, IEventBus bus, ILogger<RefundOnOutOfStockHandler> logger)
    {
        _payments = payments;
        _bus = bus;
        _logger = logger;
    }

    public async Task HandleAsync(InventoryOutOfStock e, CancellationToken ct = default)
    {
        var paymentId = await _payments.GetPaymentIdAsync(e.OrderId, ct);
        if (paymentId is null) return; // nothing was charged

        if (await _payments.RefundAsync(e.OrderId, ct))
        {
            await _bus.PublishAsync(new PaymentRefunded { OrderId = e.OrderId, PaymentId = paymentId.Value }, ct);
            _logger.LogInformation("Refunded payment {PaymentId} for order {OrderId} (compensation)",
                paymentId, e.OrderId);
        }
    }
}
