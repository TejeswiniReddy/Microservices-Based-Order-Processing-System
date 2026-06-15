using OrderFlow.Contracts.Models;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Marker for every message that travels across service boundaries on the bus.
/// EventId + OccurredAt give consumers what they need for idempotency and tracing.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

// ---- Order service publishes ----
public sealed record OrderCreated : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public List<OrderItem> Items { get; init; } = new();
}

// ---- Payment service publishes ----
public sealed record PaymentCompleted : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
}

public sealed record PaymentFailed : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record PaymentRefunded : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid PaymentId { get; init; }
}

// ---- Inventory service publishes ----
public sealed record InventoryReserved : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid ReservationId { get; init; }
}

public sealed record InventoryOutOfStock : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string Sku { get; init; } = string.Empty;
}
