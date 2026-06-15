using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.Infrastructure.Messaging;

/// <summary>
/// Production <see cref="IEventBus"/> backed by Azure Service Bus. Every
/// integration event is published to a single topic; the CLR event type name is
/// carried in the message <see cref="ServiceBusMessage.Subject"/> so consumers
/// can rehydrate the correct record type. This is the transport that orchestrates
/// the asynchronous payment / order / inventory workflows across independently
/// deployed services.
/// </summary>
public sealed class AzureServiceBusEventBus : IEventBus, IAsyncDisposable
{
    public const string TopicName = "orderflow-events";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    public AzureServiceBusEventBus(ServiceBusClient client)
    {
        _client = client;
        _sender = client.CreateSender(TopicName);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), JsonOptions);
        var message = new ServiceBusMessage(body)
        {
            Subject = @event.GetType().Name,   // used by the processor to resolve the type
            MessageId = @event.EventId.ToString(),
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(message, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

/// <summary>
/// Maps integration-event type names to their CLR types so the Service Bus
/// processor can deserialize messages back into the correct record.
/// </summary>
public static class IntegrationEventTypeMap
{
    private static readonly IReadOnlyDictionary<string, Type> ByName =
        typeof(IIntegrationEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, t => t);

    public static Type? Resolve(string subject) =>
        ByName.TryGetValue(subject, out var type) ? type : null;
}
