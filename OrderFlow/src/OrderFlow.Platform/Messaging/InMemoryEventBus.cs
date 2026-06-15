using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.Platform.Messaging;

/// <summary>
/// In-process implementation of <see cref="IEventBus"/> built on a bounded-free
/// <see cref="Channel{T}"/>. Publishing is non-blocking; a single background
/// reader dispatches each event to every registered handler in its own DI scope.
///
/// This mirrors the at-least-once, ordered delivery you get from a single
/// Azure Service Bus queue/subscription, which is why the saga behaves
/// identically whether it runs on this bus (dev/tests) or on the real
/// AzureServiceBusEventBus (production).
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly Channel<IIntegrationEvent> _channel =
        Channel.CreateUnbounded<IIntegrationEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelReader<IIntegrationEvent> Reader => _channel.Reader;

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        _channel.Writer.TryWrite(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Drains the in-memory bus and fans each event out to all
/// <see cref="IIntegrationEventHandler{TEvent}"/> registrations resolved from DI.
/// </summary>
public sealed class EventDispatcherService : BackgroundService
{
    private readonly InMemoryEventBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventDispatcherService> _logger;

    public EventDispatcherService(
        IEventBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<EventDispatcherService> logger)
    {
        _bus = (InMemoryEventBus)bus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var @event in _bus.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(@event, stoppingToken);
        }
    }

    private async Task DispatchAsync(IIntegrationEvent @event, CancellationToken ct)
    {
        var eventType = @event.GetType();
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToArray();

        if (handlers.Length == 0)
            return;

        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;

        foreach (var handler in handlers)
        {
            try
            {
                await (Task)method.Invoke(handler, new object[] { @event, ct })!;
                _logger.LogInformation("Dispatched {Event} -> {Handler}",
                    eventType.Name, handler!.GetType().Name);
            }
            catch (Exception ex)
            {
                // Production (Azure Service Bus) abandons the message for retry / dead-letter.
                _logger.LogError(ex, "Handler {Handler} failed for {Event}",
                    handler!.GetType().Name, eventType.Name);
            }
        }
    }
}
