using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace FinanceTracker.Api.Kafka;

public sealed class KafkaProducer : IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly KafkaSettings settings;

    public KafkaProducer(IOptions<KafkaSettings> options)
    {
        settings = options.Value;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 10
        };
        producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    public async Task PublishAsync(string key, string payload, CancellationToken cancellationToken)
    {
        await producer.ProduceAsync(settings.Topic, new Message<string, string> { Key = key, Value = payload }, cancellationToken);
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}
