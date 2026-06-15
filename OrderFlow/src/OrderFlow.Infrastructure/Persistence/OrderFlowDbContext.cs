using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Models;

namespace OrderFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the SQL Server-backed stores. In production each service
/// owns its own schema/database; this single context is shown for clarity and is
/// what the DevHost would use with EnableCloud=true against one database.
/// Run <c>dotnet ef migrations add Initial</c> then <c>dotnet ef database update</c>.
/// </summary>
public sealed class OrderFlowDbContext : DbContext
{
    public OrderFlowDbContext(DbContextOptions<OrderFlowDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.CustomerId).HasMaxLength(64).IsRequired();
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(o => o.StatusReason).HasMaxLength(256);
            e.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            // Order items are owned and stored in a child table.
            e.OwnsMany(o => o.Items, items =>
            {
                items.ToTable("OrderItems");
                items.WithOwner().HasForeignKey("OrderId");
                items.Property<int>("Id");
                items.HasKey("Id");
                items.Property(i => i.Sku).HasMaxLength(64).IsRequired();
                items.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            });
            e.HasIndex(o => o.CustomerId);
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Sku);
            e.Property(p => p.Sku).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasColumnType("decimal(18,2)");
            // Optimistic concurrency so concurrent reservations don't oversell.
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        b.Entity<PaymentRecord>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(p => p.PaymentId);
            e.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            e.HasIndex(p => p.OrderId).IsUnique();
        });
    }
}

/// <summary>Persisted payment row (kept in Infrastructure to avoid leaking EF into Contracts).</summary>
public sealed class PaymentRecord
{
    public Guid PaymentId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool Refunded { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
