using System.Security.Claims;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts.Models;
using OrderFlow.InventoryService.Persistence;
using OrderFlow.Platform.Security;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class JwtTokenServiceTests
{
    private static JwtTokenService NewService() =>
        new(Options.Create(new JwtOptions { Secret = "test-secret-key-that-is-long-enough-0123456789", ExpiryMinutes = 30 }));

    [Fact]
    public void Token_roundtrips_with_claims()
    {
        var svc = NewService();
        var (token, expiresIn) = svc.CreateToken("cust-alice", "alice", Roles.Customer);

        Assert.True(expiresIn > 0);
        Assert.True(svc.TryValidate(token, out var principal));
        Assert.NotNull(principal);
        Assert.Equal("cust-alice", principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole(Roles.Customer));
        Assert.False(principal.IsInRole(Roles.Admin));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var svc = NewService();
        var (token, _) = svc.CreateToken("cust-alice", "alice", Roles.Customer);
        var tampered = token[..^2] + (token[^1] == 'A' ? "BB" : "AA");

        Assert.False(svc.TryValidate(tampered, out var principal));
        Assert.Null(principal);
    }

    [Fact]
    public void Garbage_token_is_rejected()
    {
        var svc = NewService();
        Assert.False(svc.TryValidate("not.a.jwt", out _));
    }
}

public class UserStoreTests
{
    [Theory]
    [InlineData("alice", "password", true)]
    [InlineData("admin", "admin", true)]
    [InlineData("alice", "wrong", false)]
    [InlineData("nobody", "password", false)]
    public void Validate_matches_only_correct_credentials(string user, string pass, bool expected)
    {
        var store = new UserStore();
        Assert.Equal(expected, store.Validate(user, pass) is not null);
    }
}

public class InventoryReservationTests
{
    [Fact]
    public async Task Reserve_succeeds_when_stock_is_available()
    {
        var repo = new InMemoryInventoryRepository();
        var ok = await repo.TryReserveAsync(new[] { new OrderItem { Sku = "BOOK-001", Quantity = 2, UnitPrice = 39.99m } });
        Assert.True(ok);
    }

    [Fact]
    public async Task Reserve_is_all_or_nothing()
    {
        var repo = new InMemoryInventoryRepository();
        // RARE-001 has stock 1; this whole reservation must fail and not touch BOOK-001.
        var ok = await repo.TryReserveAsync(new[]
        {
            new OrderItem { Sku = "BOOK-001", Quantity = 1, UnitPrice = 39.99m },
            new OrderItem { Sku = "RARE-001", Quantity = 2, UnitPrice = 199m }
        });
        Assert.False(ok);

        var book = await repo.GetAsync("BOOK-001");
        Assert.Equal(50, book!.StockOnHand); // unchanged
    }

    [Fact]
    public async Task Concurrent_reservations_never_oversell()
    {
        var repo = new InMemoryInventoryRepository(); // RARE-001 stock = 1
        var item = new[] { new OrderItem { Sku = "RARE-001", Quantity = 1, UnitPrice = 199m } };

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => repo.TryReserveAsync(item)));

        Assert.Equal(1, results.Count(r => r));      // exactly one wins
        var rare = await repo.GetAsync("RARE-001");
        Assert.Equal(0, rare!.StockOnHand);          // never negative
    }
}
