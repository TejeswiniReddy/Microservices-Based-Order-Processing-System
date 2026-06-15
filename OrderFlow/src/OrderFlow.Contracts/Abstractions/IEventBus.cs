using OrderFlow.Contracts.Events;

namespace OrderFlow.Contracts.Abstractions;

/// <summary>
/// Transport-agnostic publish/subscribe bus. The in-memory implementation
/// (OrderFlow.Platform) is used for local/dev and tests; the Azure Service Bus
/// implementation (OrderFlow.Infrastructure) is used in production. Services
/// only ever depend on this interface.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>Handles a single integration event type.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class, IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Abstraction over the distributed cache. Backed by an in-process store for
/// dev/tests and by Redis (StackExchange.Redis) in production.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}
