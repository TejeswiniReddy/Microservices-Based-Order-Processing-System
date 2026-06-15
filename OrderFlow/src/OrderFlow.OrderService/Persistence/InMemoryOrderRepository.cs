using System.Collections.Concurrent;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Models;

namespace OrderFlow.OrderService.Persistence;

/// <summary>
/// Thread-safe in-memory order store for dev/tests. Production uses the EF Core
/// SQL Server repository in OrderFlow.Infrastructure (same IOrderRepository contract).
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_orders.TryGetValue(id, out var o) ? o : null);

    public Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(_orders.Values.OrderByDescending(o => o.CreatedAt).ToList());

    public Task<IReadOnlyList<Order>> ListByCustomerAsync(string customerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(
            _orders.Values.Where(o => o.CustomerId == customerId)
                          .OrderByDescending(o => o.CreatedAt).ToList());

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}
