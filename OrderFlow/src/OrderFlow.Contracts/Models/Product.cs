namespace OrderFlow.Contracts.Models;

/// <summary>
/// Catalog product owned by the Inventory service. Stock is decremented when a
/// reservation succeeds and restored on compensation.
/// </summary>
public sealed class Product
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockOnHand { get; set; }
}
