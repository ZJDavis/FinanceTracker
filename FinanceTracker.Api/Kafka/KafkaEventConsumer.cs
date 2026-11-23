using System.Text.Json;
using Confluent.Kafka;
using FinanceTracker.Data.Postgres;
using FinanceTracker.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceTracker.Api.Kafka;

/// <summary>
/// Demonstrates an idempotent consumer. Kafka offsets are committed only after
/// the event receipt is saved to PostgreSQL.
/// </summary>
public sealed class KafkaEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> options,
    ILogger<KafkaEventConsumer> logger) : BackgroundService
{
    private readonly KafkaSettings settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            GroupId = settings.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(settings.Topic);
        logger.LogInformation("Kafka consumer listening to {Topic} as group {ConsumerGroup}", settings.Topic, settings.ConsumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = consumer.Consume(TimeSpan.FromSeconds(1));
                if (message is null) continue;

                await SaveReceiptIfNewAsync(message.Message.Value, stoppingToken);
                consumer.Commit(message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException exception)
            {
                logger.LogWarning(exception, "Kafka could not deliver a message.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (Exception exception)
            {
                // Do not commit an event that failed to persist. Kafka will offer it again.
                logger.LogError(exception, "A Kafka event could not be recorded; it will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task SaveReceiptIfNewAsync(string payload, CancellationToken cancellationToken)
    {
        using var eventDocument = JsonDocument.Parse(payload);
        var root = eventDocument.RootElement;
        var eventId = root.GetProperty("eventId").GetGuid();
        var eventType = root.GetProperty("eventType").GetString() ?? "unknown";
        var occurredAtUtc = root.GetProperty("occurredAtUtc").GetDateTimeOffset();

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        if (await database.ProcessedEvents.AnyAsync(receipt => receipt.EventId == eventId, cancellationToken))
            return;

        database.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A second delivery may race the first one. Only suppress the error
            // when the unique event id is now present; other database errors retry.
            if (!await database.ProcessedEvents.AnyAsync(receipt => receipt.EventId == eventId, cancellationToken))
                throw;
        }
    }
}
