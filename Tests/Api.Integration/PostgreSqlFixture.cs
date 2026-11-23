using FinanceTracker.Data.Postgres;
using FinanceTracker.Data.Postgres.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FinanceTracker.Api.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer databaseContainer = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("financetracker_tests")
        .WithUsername("finance_tests")
        .WithPassword("finance_tests_only")
        .Build();

    private DbContextOptions<FinanceDbContext> databaseOptions = null!;

    public FinanceApiFactory ApiFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await databaseContainer.StartAsync();
        databaseOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(databaseContainer.GetConnectionString())
            .Options;

        await using (var database = new FinanceDbContext(databaseOptions))
        {
            await database.Database.MigrateAsync();
            await DemoDataSeeder.SeedAsync(database);
        }

        ApiFactory = new FinanceApiFactory(databaseContainer.GetConnectionString());
    }

    public async Task ResetTransactionsAsync()
    {
        await using var database = new FinanceDbContext(databaseOptions);
        await database.Database.ExecuteSqlRawAsync("TRUNCATE TABLE transactions, outbox_messages, processed_events RESTART IDENTITY");
    }

    public async Task<IReadOnlyList<string>> GetOutboxEventTypesAsync()
    {
        await using var database = new FinanceDbContext(databaseOptions);
        return await database.OutboxMessages.Select(message => message.EventType).ToListAsync();
    }

    public Task DisposeAsync()
    {
        ApiFactory.Dispose();
        return databaseContainer.DisposeAsync().AsTask();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL API integration tests";
}
