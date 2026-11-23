using FinanceTracker.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceTracker.Api.Kafka;

/// <summary>
/// Publishes committed outbox rows. A crash after Kafka accepts a message but
/// before PostgreSQL marks it published can cause a duplicate; consumers dedupe by event id.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    KafkaProducer kafkaProducer,
    IOptions<KafkaSettings> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly string topic = options.Value.Topic;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publishedAny = await PublishPendingMessagesAsync(stoppingToken);
                if (!publishedAny)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox publishing is waiting for Kafka or PostgreSQL.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var pendingMessages = await database.OutboxMessages
            .Where(message => message.PublishedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0) return false;

        foreach (var pendingMessage in pendingMessages)
        {
            try
            {
                await kafkaProducer.PublishAsync(pendingMessage.AggregateId, pendingMessage.Payload, cancellationToken);
                pendingMessage.PublishedAtUtc = DateTimeOffset.UtcNow;
                pendingMessage.LastError = null;
                pendingMessage.PublishAttempts++;
                await database.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Published {EventType} event {EventId} to {Topic}", pendingMessage.EventType, pendingMessage.Id, topic);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                pendingMessage.PublishAttempts++;
                pendingMessage.LastError = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
                await database.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        return true;
    }
}
