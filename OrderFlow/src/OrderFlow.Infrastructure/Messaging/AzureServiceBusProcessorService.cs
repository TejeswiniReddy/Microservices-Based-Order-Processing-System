using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Abstractions;
using OrderFlow.Contracts.Events;

namespace OrderFlow.Infrastructure.Messaging;

/// <summary>
/// Consumes integration events from the Azure Service Bus subscription and fans
/// each one out to every registered <see cref="IIntegrationEventHandler{TEvent}"/>,
/// mirroring the dev EventDispatcherService exactly. A handler exception abandons
/// the message so Service Bus redelivers it (and ultimately dead-letters it after
/// max delivery count), giving the saga at-least-once delivery and retries.
///
/// Each deployed service sets <c>SubscriptionName</c> to its own subscription on
/// the shared topic, so every service sees the events it cares about.
/// </summary>
public sealed class AzureServiceBusProcessorService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AzureServiceBusProcessorService> _logger;
    private readonly string _subscriptionName;
    private ServiceBusProcessor? _processor;

    public AzureServiceBusProcessorService(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        ServiceBusSubscriptionOptions options,
        ILogger<AzureServiceBusProcessorService> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _subscriptionName = options.SubscriptionName;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(
            AzureServiceBusEventBus.TopicName,
            _subscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 4
            });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Service Bus processor error on {Entity}", args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var subject = args.Message.Subject;
        var clrType = IntegrationEventTypeMap.Resolve(subject);
        if (clrType is null)
        {
            _logger.LogWarning("No CLR type for event subject {Subject}; dead-lettering", subject);
            await args.DeadLetterMessageAsync(args.Message, "UnknownEventType");
            return;
        }

        IIntegrationEvent? @event;
        try
        {
            @event = (IIntegrationEvent?)JsonSerializer.Deserialize(
                args.Message.Body.ToString(), clrType, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {Subject}; dead-lettering", subject);
            await args.DeadLetterMessageAsync(args.Message, "DeserializationError");
            return;
        }

        if (@event is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "NullEvent");
            return;
        }

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(clrType);
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;

        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToArray();

        try
        {
            foreach (var handler in handlers)
                await (Task)method.Invoke(handler!, new object[] { @event, args.CancellationToken })!;

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            // Abandon -> Service Bus redelivers; after max delivery count it dead-letters.
            _logger.LogError(ex, "Handler failed for {Subject}; abandoning for retry", subject);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>Per-service subscription name on the shared events topic.</summary>
public sealed class ServiceBusSubscriptionOptions
{
    public string SubscriptionName { get; init; } = "default";
}
