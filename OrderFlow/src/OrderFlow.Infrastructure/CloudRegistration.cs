using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Messaging;
using OrderFlow.Infrastructure.Persistence;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure;

public static class CloudRegistration
{
    /// <summary>
    /// Replaces the in-memory dev providers with their production counterparts,
    /// chosen per the Providers section of configuration:
    ///   Providers:Cache   = Redis      -> RedisCacheService
    ///   Providers:Bus     = ServiceBus -> AzureServiceBusEventBus + processor
    ///   Providers:Storage = SqlServer  -> EF Core SQL Server repositories
    /// Each provider is swapped independently, so you can run, say, Redis + the
    /// in-memory bus while developing. Called from each service's Program.cs
    /// inside <c>#if CLOUD</c>.
    /// </summary>
    public static IServiceCollection AddOrderFlowCloud(this IServiceCollection services, IConfiguration config)
    {
        var providers = config.GetSection("Providers");

        if (string.Equals(providers["Cache"], "Redis", StringComparison.OrdinalIgnoreCase))
        {
            var conn = config.GetConnectionString("Redis")
                       ?? throw new InvalidOperationException("ConnectionStrings:Redis is required for Redis cache.");
            services.RemoveAll<ICacheService>();
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(conn));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }

        if (string.Equals(providers["Bus"], "ServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            var conn = config.GetConnectionString("ServiceBus")
                       ?? throw new InvalidOperationException("ConnectionStrings:ServiceBus is required for Azure Service Bus.");
            var subscription = providers["Subscription"] ?? "default";

            // Remove the in-memory bus + its hosted dispatcher.
            services.RemoveAll<IEventBus>();
            services.RemoveAll<InMemoryEventBusMarker>();

            services.AddSingleton(_ => new ServiceBusClient(conn));
            services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
            services.AddSingleton(new ServiceBusSubscriptionOptions { SubscriptionName = subscription });
            services.AddHostedService<AzureServiceBusProcessorService>();
        }

        if (string.Equals(providers["Storage"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var conn = config.GetConnectionString("SqlServer")
                       ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is required for SQL Server storage.");

            services.AddDbContext<OrderFlowDbContext>(o => o.UseSqlServer(conn));

            services.RemoveAll<IOrderRepository>();
            services.RemoveAll<IPaymentRepository>();
            services.RemoveAll<IInventoryRepository>();
            services.AddScoped<IOrderRepository, SqlOrderRepository>();
            services.AddScoped<IPaymentRepository, SqlPaymentRepository>();
            services.AddScoped<IInventoryRepository, SqlInventoryRepository>();
        }

        return services;
    }
}

/// <summary>
/// Marker only used to express intent in code; the in-memory dispatcher is a
/// hosted service and is removed implicitly when the bus is swapped at the host
/// level. Present so the swap reads clearly.
/// </summary>
internal sealed class InMemoryEventBusMarker { }
