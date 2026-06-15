using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.Platform.Messaging;

public static class MessagingRegistration
{
    /// <summary>
    /// Registers the in-process event bus and its background dispatcher.
    /// The production path (EnableCloud=true) replaces the IEventBus registration
    /// with AzureServiceBusEventBus from OrderFlow.Infrastructure.
    /// </summary>
    public static IServiceCollection AddInMemoryEventBus(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryEventBus>();
        services.TryAddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        services.AddHostedService<EventDispatcherService>();
        return services;
    }

    /// <summary>Registers a handler for a given integration event type.</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }
}
