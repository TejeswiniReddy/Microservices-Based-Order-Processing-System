namespace OrderFlow.Contracts.Abstractions;

/// <summary>
/// Centralized cache key conventions shared across services so producers and
/// consumers never drift. Backed by the in-process cache in dev/tests and by
/// Redis in production.
/// </summary>
public static class CacheKeys
{
    /// <summary>Cached product catalog (Inventory service).</summary>
    public const string Catalog = "catalog:all";

    /// <summary>Line items for an order, stamped when the order is created.</summary>
    public static string OrderItems(Guid orderId) => $"order:{orderId}:items";
}
