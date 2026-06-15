namespace OrderFlow.Contracts.Models;

/// <summary>
/// Lifecycle of an order as it moves through the distributed saga.
/// Transitions are driven entirely by integration events published on the bus.
/// </summary>
public enum OrderStatus
{
    Pending = 0,            // created, awaiting payment
    PaymentProcessing = 1,  // OrderCreated published, payment in flight
    PaymentCompleted = 2,   // payment captured, awaiting inventory
    Confirmed = 3,          // inventory reserved -> terminal success
    Cancelled = 4           // payment failed / out of stock -> terminal failure
}

public sealed class OrderItem
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = new();
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? StatusReason { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ReservationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public decimal TotalAmount => Items.Sum(i => i.LineTotal);
}
