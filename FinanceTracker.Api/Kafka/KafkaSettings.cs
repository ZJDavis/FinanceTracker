namespace FinanceTracker.Api.Kafka;

public sealed class KafkaSettings
{
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "finance.transaction-events.v1";
    public string ConsumerGroup { get; set; } = "finance-tracker-demo";
}
