namespace FinanceTracker.Data.Postgres.Entities;

public sealed class AccountRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NameKey { get; set; }
    public required string Type { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TransactionRecord> Transactions { get; set; } = [];
}

public sealed class CategoryRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NameKey { get; set; }
    public required string Kind { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TransactionRecord> Transactions { get; set; } = [];
}

public sealed class TransactionRecord
{
    public int Id { get; set; }
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public DateOnly Date { get; set; }
    public int AccountId { get; set; }
    public AccountRecord Account { get; set; } = null!;
    public int CategoryId { get; set; }
    public CategoryRecord Category { get; set; } = null!;
    public required string Payee { get; set; }
    public string Memo { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public required string AggregateType { get; set; }
    public required string AggregateId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int PublishAttempts { get; set; }
    public string? LastError { get; set; }
}

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
