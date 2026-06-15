using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Models;

namespace OrderFlow.InventoryService.Controllers;

/// <summary>
/// Product catalog. GET is available to any authenticated customer and is served
/// through the distributed cache (Redis in production) — the first read is a MISS
/// that populates the cache, subsequent reads are HITs. The response carries an
/// <c>X-Cache</c> header so the behaviour is observable. Writes are admin-only
/// (RBAC) and invalidate the cache.
/// </summary>
[ApiController]
[Route("catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly IInventoryRepository _inventory;
    private readonly ICacheService _cache;

    public CatalogController(IInventoryRepository inventory, ICacheService cache)
    {
        _inventory = inventory;
        _cache = cache;
    }

    /// <summary>List the catalog. Cached; sets X-Cache: HIT|MISS.</summary>
    [HttpGet]
    [Authorize(Policy = "Customers")]
    public async Task<ActionResult<IReadOnlyList<Product>>> Get(CancellationToken ct)
    {
        var cached = await _cache.GetAsync<List<Product>>(CacheKeys.Catalog, ct);
        if (cached is not null)
        {
            Response.Headers["X-Cache"] = "HIT";
            return Ok(cached);
        }

        var catalog = (await _inventory.GetCatalogAsync(ct)).ToList();
        await _cache.SetAsync(CacheKeys.Catalog, catalog, TimeSpan.FromMinutes(5), ct);
        Response.Headers["X-Cache"] = "MISS";
        return Ok(catalog);
    }

    /// <summary>Create or update a product. Admin only; invalidates the cache.</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<Product>> Upsert([FromBody] Product product, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(product.Sku))
            return BadRequest(new { error = "sku_required" });

        await _inventory.UpsertAsync(product, ct);
        await _cache.RemoveAsync(CacheKeys.Catalog, ct);
        return Ok(product);
    }
}
