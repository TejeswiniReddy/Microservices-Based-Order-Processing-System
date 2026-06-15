using System.Collections.Concurrent;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Models;

namespace OrderFlow.InventoryService.Persistence;

/// <summary>
/// In-memory catalog + stock with all-or-nothing reservation. A lock guarantees
/// the reserve check-and-decrement is atomic across concurrent saga events.
/// Production uses EF Core + SQL Server with a transaction / row versioning.
/// </summary>
public sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly ConcurrentDictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public InMemoryInventoryRepository()
    {
        Seed(new Product { Sku = "BOOK-001",   Name = "The Pragmatic Programmer", Price = 39.99m, StockOnHand = 50 });
        Seed(new Product { Sku = "LAPTOP-001", Name = "Developer Laptop 14\"",     Price = 1499.00m, StockOnHand = 5 });
        Seed(new Product { Sku = "MOUSE-001",  Name = "Wireless Mouse",            Price = 24.99m, StockOnHand = 200 });
        Seed(new Product { Sku = "RARE-001",   Name = "Limited Edition Keyboard",  Price = 199.00m, StockOnHand = 1 });
    }

    private void Seed(Product p) => _products[p.Sku] = p;

    public Task<IReadOnlyList<Product>> GetCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Product>>(_products.Values.OrderBy(p => p.Sku).ToList());

    public Task<Product?> GetAsync(string sku, CancellationToken ct = default) =>
        Task.FromResult(_products.TryGetValue(sku, out var p) ? p : null);

    public Task UpsertAsync(Product product, CancellationToken ct = default)
    {
        _products[product.Sku] = product;
        return Task.CompletedTask;
    }

    public Task<bool> TryReserveAsync(IEnumerable<OrderItem> items, CancellationToken ct = default)
    {
        var list = items.ToList();
        lock (_gate)
        {
            // Verify everything is available before mutating anything.
            foreach (var item in list)
            {
                if (!_products.TryGetValue(item.Sku, out var p) || p.StockOnHand < item.Quantity)
                    return Task.FromResult(false);
            }
            foreach (var item in list)
                _products[item.Sku].StockOnHand -= item.Quantity;

            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var item in items)
                if (_products.TryGetValue(item.Sku, out var p))
                    p.StockOnHand += item.Quantity;
        }
        return Task.CompletedTask;
    }
}
