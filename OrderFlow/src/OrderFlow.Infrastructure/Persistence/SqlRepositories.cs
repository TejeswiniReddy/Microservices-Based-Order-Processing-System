using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Models;

namespace OrderFlow.Infrastructure.Persistence;

/// <summary>EF Core + SQL Server implementation of <see cref="IOrderRepository"/>.</summary>
public sealed class SqlOrderRepository : IOrderRepository
{
    private readonly OrderFlowDbContext _db;
    public SqlOrderRepository(OrderFlowDbContext db) => _db = db;

    public async Task<Order?> GetAsync(Guid id, CancellationToken ct = default) =>
        await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default) =>
        await _db.Orders.Include(o => o.Items).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> ListByCustomerAsync(string customerId, CancellationToken ct = default) =>
        await _db.Orders.Include(o => o.Items)
                        .Where(o => o.CustomerId == customerId)
                        .OrderByDescending(o => o.CreatedAt)
                        .ToListAsync(ct);

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Orders.Update(order);
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>EF Core + SQL Server implementation of <see cref="IPaymentRepository"/>.</summary>
public sealed class SqlPaymentRepository : IPaymentRepository
{
    private readonly OrderFlowDbContext _db;
    public SqlPaymentRepository(OrderFlowDbContext db) => _db = db;

    public async Task<Guid> RecordChargeAsync(Guid orderId, decimal amount, CancellationToken ct = default)
    {
        var rec = new PaymentRecord { OrderId = orderId, Amount = amount };
        _db.Payments.Add(rec);
        await _db.SaveChangesAsync(ct);
        return rec.PaymentId;
    }

    public async Task<bool> RefundAsync(Guid orderId, CancellationToken ct = default)
    {
        var rec = await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (rec is null || rec.Refunded) return false;
        rec.Refunded = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid?> GetPaymentIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var rec = await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        return rec?.PaymentId;
    }
}

/// <summary>
/// EF Core + SQL Server implementation of <see cref="IInventoryRepository"/>.
/// Reservation runs in a serializable transaction and relies on the Product
/// RowVersion for optimistic concurrency so two concurrent sagas cannot oversell.
/// </summary>
public sealed class SqlInventoryRepository : IInventoryRepository
{
    private readonly OrderFlowDbContext _db;
    public SqlInventoryRepository(OrderFlowDbContext db) => _db = db;

    public async Task<IReadOnlyList<Product>> GetCatalogAsync(CancellationToken ct = default) =>
        await _db.Products.OrderBy(p => p.Sku).ToListAsync(ct);

    public async Task<Product?> GetAsync(string sku, CancellationToken ct = default) =>
        await _db.Products.FirstOrDefaultAsync(p => p.Sku == sku, ct);

    public async Task UpsertAsync(Product product, CancellationToken ct = default)
    {
        var existing = await _db.Products.FirstOrDefaultAsync(p => p.Sku == product.Sku, ct);
        if (existing is null) _db.Products.Add(product);
        else { existing.Name = product.Name; existing.Price = product.Price; existing.StockOnHand = product.StockOnHand; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryReserveAsync(IEnumerable<OrderItem> items, CancellationToken ct = default)
    {
        var list = items.ToList();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            foreach (var item in list)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Sku == item.Sku, ct);
                if (product is null || product.StockOnHand < item.Quantity)
                {
                    await tx.RollbackAsync(ct);
                    return false;
                }
            }
            foreach (var item in list)
            {
                var product = await _db.Products.FirstAsync(p => p.Sku == item.Sku, ct);
                product.StockOnHand -= item.Quantity;
            }
            await _db.SaveChangesAsync(ct);   // throws DbUpdateConcurrencyException on a stale RowVersion
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
    }

    public async Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Sku == item.Sku, ct);
            if (product is not null) product.StockOnHand += item.Quantity;
        }
        await _db.SaveChangesAsync(ct);
    }
}
