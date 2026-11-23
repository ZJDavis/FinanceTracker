using FinanceTracker.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Data.Postgres;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();
    public DbSet<CategoryRecord> Categories => Set<CategoryRecord>();
    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountRecord>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Name).HasMaxLength(100).IsRequired();
            entity.Property(account => account.NameKey).HasMaxLength(100).IsRequired();
            entity.Property(account => account.Type).HasMaxLength(50).IsRequired();
            entity.Property(account => account.OpeningBalance).HasColumnType("numeric");
            entity.HasIndex(account => account.NameKey).IsUnique();
        });

        modelBuilder.Entity<CategoryRecord>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).HasMaxLength(100).IsRequired();
            entity.Property(category => category.NameKey).HasMaxLength(100).IsRequired();
            entity.Property(category => category.Kind).HasMaxLength(20).IsRequired();
            entity.HasIndex(category => category.NameKey).IsUnique();
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("transactions", table =>
            {
                table.HasCheckConstraint("ck_transactions_nonzero_amount", "amount <> 0");
                table.HasCheckConstraint("ck_transactions_positive_version", "version > 0");
            });
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.SyncId)
                .HasColumnName("sync_id")
                .HasDefaultValueSql("gen_random_uuid()")
                .IsRequired();
            entity.HasIndex(transaction => transaction.SyncId).IsUnique().HasDatabaseName("ux_transactions_sync_id");
            entity.Property(transaction => transaction.Date).HasColumnType("date");
            entity.Property(transaction => transaction.Payee).HasMaxLength(200).IsRequired();
            entity.Property(transaction => transaction.Memo).HasMaxLength(2000).IsRequired();
            entity.Property(transaction => transaction.Amount).HasColumnName("amount").HasColumnType("numeric");
            entity.Property(transaction => transaction.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(transaction => transaction.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.UpdatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne(transaction => transaction.Account).WithMany(account => account.Transactions).HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.Category).WithMany(category => category.Transactions).HasForeignKey(transaction => transaction.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(transaction => new { transaction.AccountId, transaction.Date, transaction.Id }).HasDatabaseName("ix_transactions_account_date_id");
            entity.HasIndex(transaction => new { transaction.CategoryId, transaction.Date }).HasDatabaseName("ix_transactions_category_date");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.EventType).HasMaxLength(100).IsRequired();
            entity.Property(message => message.AggregateType).HasMaxLength(100).IsRequired();
            entity.Property(message => message.AggregateId).HasMaxLength(100).IsRequired();
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.LastError).HasMaxLength(2000);
            entity.HasIndex(message => new { message.PublishedAtUtc, message.OccurredAtUtc });
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(receipt => receipt.EventId);
            entity.Property(receipt => receipt.EventType).HasMaxLength(100).IsRequired();
            entity.Property(receipt => receipt.Payload).IsRequired();
            entity.HasIndex(receipt => receipt.ReceivedAtUtc);
        });
    }
}
