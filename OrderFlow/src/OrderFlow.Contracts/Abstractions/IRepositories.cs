using OrderFlow.Contracts.Models;

namespace OrderFlow.Contracts.Abstractions;

/// <summary>
/// Persistence for orders. In-memory for dev/tests; EF Core + SQL Server in
/// production (OrderFlow.Infrastructure).
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Order>> ListByCustomerAsync(string customerId, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task UpdateAsync(Order order, CancellationToken ct = default);
}

/// <summary>Persistence for payment records.</summary>
public interface IPaymentRepository
{
    Task<Guid> RecordChargeAsync(Guid orderId, decimal amount, CancellationToken ct = default);
    Task<bool> RefundAsync(Guid orderId, CancellationToken ct = default);
    Task<Guid?> GetPaymentIdAsync(Guid orderId, CancellationToken ct = default);
}

/// <summary>Catalog + stock store for the Inventory service.</summary>
public interface IInventoryRepository
{
    Task<IReadOnlyList<Product>> GetCatalogAsync(CancellationToken ct = default);
    Task<Product?> GetAsync(string sku, CancellationToken ct = default);
    Task UpsertAsync(Product product, CancellationToken ct = default);

    /// <summary>Atomically reserve stock for all items, or none. Returns true on success.</summary>
    Task<bool> TryReserveAsync(IEnumerable<OrderItem> items, CancellationToken ct = default);

    /// <summary>Compensation: return previously reserved stock.</summary>
    Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken ct = default);
}
