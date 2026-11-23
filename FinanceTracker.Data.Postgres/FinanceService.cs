using System.Text.Json;
using FinanceTracker.Application.Contracts;
using FinanceTracker.Application.Rules;
using FinanceTracker.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Data.Postgres;

/// <summary>
/// Implements the finance use cases against PostgreSQL. Transaction changes and
/// their outbox messages are saved in the same database transaction.
/// </summary>
public sealed class FinanceService(FinanceDbContext database) : IFinanceService
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<AccountRecord> accounts = database.Accounts.AsNoTracking();
        if (!includeInactive) accounts = accounts.Where(account => account.IsActive);

        return await accounts.OrderBy(account => account.Id)
            .Select(account => new AccountResponse(account.Id, account.Name, account.Type, account.OpeningBalance, account.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<CategoryRecord> categories = database.Categories.AsNoTracking();
        if (!includeInactive) categories = categories.Where(category => category.IsActive);

        return await categories.OrderBy(category => category.Id)
            .Select(category => new CategoryResponse(category.Id, category.Name, category.Kind, category.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountResponse> GetAccountAsync(int id, CancellationToken cancellationToken)
    {
        var account = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new FinanceNotFoundException($"Account {id} was not found.");
        return new AccountResponse(account.Id, account.Name, account.Type, account.OpeningBalance, account.IsActive);
    }

    public async Task<CategoryResponse> GetCategoryAsync(int id, CancellationToken cancellationToken)
    {
        var category = await database.Categories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new FinanceNotFoundException($"Category {id} was not found.");
        return new CategoryResponse(category.Id, category.Name, category.Kind, category.IsActive);
    }

    public async Task<PageResponse<TransactionResponse>> GetTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken)
    {
        FinanceRules.ValidateTransactionQuery(query);
        var transactions = database.Transactions.AsNoTracking().AsQueryable();

        if (query.AccountId is int accountId) transactions = transactions.Where(transaction => transaction.AccountId == accountId);
        if (query.CategoryId is int categoryId) transactions = transactions.Where(transaction => transaction.CategoryId == categoryId);
        if (query.From is DateOnly from) transactions = transactions.Where(transaction => transaction.Date >= from);
        if (query.To is DateOnly to) transactions = transactions.Where(transaction => transaction.Date <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var safeSearch = EscapeLikePattern(query.Search.Trim());
            var pattern = $"%{safeSearch}%";
            transactions = transactions.Where(transaction =>
                EF.Functions.ILike(transaction.Payee, pattern, "\\") ||
                EF.Functions.ILike(transaction.Memo, pattern, "\\"));
        }

        var totalCount = await transactions.CountAsync(cancellationToken);
        var orderedTransactions = ApplySort(transactions, query.Sort, query.Direction);
        var transactionRecords = await orderedTransactions
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = transactionRecords.Select(ToResponse).ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new PageResponse<TransactionResponse>(items, query.Page, query.PageSize, totalCount, totalPages);
    }

    public async Task<TransactionResponse> GetTransactionAsync(int id, CancellationToken cancellationToken)
    {
        var transaction = await database.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return transaction is null
            ? throw new FinanceNotFoundException($"Transaction {id} was not found.")
            : ToResponse(transaction);
    }

    public async Task<TransactionResponse> CreateTransactionAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var (account, category) = await ValidateRequestAsync(request, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var transaction = new TransactionRecord
        {
            SyncId = request.SyncId ?? Guid.NewGuid(),
            Date = request.Date,
            AccountId = account.Id,
            CategoryId = category.Id,
            Payee = request.Payee.Trim(),
            Memo = (request.Memo ?? "").Trim(),
            Amount = request.Amount,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using var dbTransaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync(cancellationToken);

        var response = ToResponse(transaction);
        AddOutboxMessage("transaction.created", transaction.Id, response, now);
        await database.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<TransactionResponse> UpdateTransactionAsync(int id, int expectedVersion, TransactionRequest request, CancellationToken cancellationToken)
    {
        var transaction = await database.Transactions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new FinanceNotFoundException($"Transaction {id} was not found.");

        if (transaction.Version != expectedVersion)
            throw new FinanceConcurrencyException($"Transaction {id} has changed since it was loaded.");

        var (account, category) = await ValidateRequestAsync(request, cancellationToken);
        transaction.Date = request.Date;
        transaction.AccountId = account.Id;
        transaction.CategoryId = category.Id;
        transaction.Payee = request.Payee.Trim();
        transaction.Memo = (request.Memo ?? "").Trim();
        transaction.Amount = request.Amount;
        transaction.Version++;
        transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var response = ToResponse(transaction);
        AddOutboxMessage("transaction.updated", transaction.Id, response, transaction.UpdatedAtUtc);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FinanceConcurrencyException($"Transaction {id} has changed since it was loaded.");
        }

        return response;
    }

    public async Task DeleteTransactionAsync(int id, int expectedVersion, CancellationToken cancellationToken)
    {
        var transaction = await database.Transactions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new FinanceNotFoundException($"Transaction {id} was not found.");

        if (transaction.Version != expectedVersion)
            throw new FinanceConcurrencyException($"Transaction {id} has changed since it was loaded.");

        var deletedResponse = ToResponse(transaction);
        var now = DateTimeOffset.UtcNow;
        AddOutboxMessage("transaction.deleted", transaction.Id, deletedResponse, now);
        database.Transactions.Remove(transaction);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FinanceConcurrencyException($"Transaction {id} has changed since it was loaded.");
        }
    }

    public async Task<ActivityResponse> GetActivityAsync(DateOnly from, DateOnly to, int? accountId, CancellationToken cancellationToken)
    {
        FinanceRules.ValidateDateRange(from, to);
        var transactions = database.Transactions.AsNoTracking()
            .Where(transaction => transaction.Date >= from && transaction.Date <= to);
        if (accountId is int selectedAccountId)
            transactions = transactions.Where(transaction => transaction.AccountId == selectedAccountId);

        var totals = await transactions.GroupBy(_ => 1)
            .Select(group => new
            {
                Income = group.Where(transaction => transaction.Amount > 0).Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                Expenses = group.Where(transaction => transaction.Amount < 0).Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                NetActivity = group.Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TransactionCount = group.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new ActivityResponse(from, to, accountId, totals?.Income ?? 0m, -(totals?.Expenses ?? 0m),
            totals?.NetActivity ?? 0m, totals?.TransactionCount ?? 0);
    }

    public async Task<IReadOnlyList<EventReceiptResponse>> GetRecentEventReceiptsAsync(int limit, CancellationToken cancellationToken)
    {
        return await database.ProcessedEvents.AsNoTracking()
            .OrderByDescending(receipt => receipt.ReceivedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(receipt => new EventReceiptResponse(
                receipt.EventId.ToString(), receipt.EventType, receipt.OccurredAtUtc, receipt.ReceivedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<(AccountRecord Account, CategoryRecord Category)> ValidateRequestAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var account = await database.Accounts.SingleOrDefaultAsync(item => item.Id == request.AccountId, cancellationToken);
        var category = await database.Categories.SingleOrDefaultAsync(item => item.Id == request.CategoryId, cancellationToken);
        FinanceRules.ValidateTransaction(request, account is not null, account?.IsActive ?? false,
            category is not null, category?.IsActive ?? false, category?.Kind ?? "");

        return (account!, category!);
    }

    private static IQueryable<TransactionRecord> ApplySort(IQueryable<TransactionRecord> transactions, string sort, string direction)
    {
        var descending = direction == "desc";
        return (sort, descending) switch
        {
            ("amount", false) => transactions.OrderBy(item => item.Amount).ThenBy(item => item.Id),
            ("amount", true) => transactions.OrderByDescending(item => item.Amount).ThenByDescending(item => item.Id),
            ("payee", false) => transactions.OrderBy(item => item.Payee).ThenBy(item => item.Id),
            ("payee", true) => transactions.OrderByDescending(item => item.Payee).ThenByDescending(item => item.Id),
            ("date", false) => transactions.OrderBy(item => item.Date).ThenBy(item => item.Id),
            _ => transactions.OrderByDescending(item => item.Date).ThenByDescending(item => item.Id)
        };
    }

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private void AddOutboxMessage(string eventType, int transactionId, TransactionResponse transaction, DateTimeOffset occurredAtUtc)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            eventType,
            occurredAtUtc,
            aggregateType = "transaction",
            aggregateId = transactionId.ToString(),
            transaction
        }, EventJsonOptions);

        database.OutboxMessages.Add(new OutboxMessage
        {
            Id = eventId,
            EventType = eventType,
            AggregateType = "transaction",
            AggregateId = transactionId.ToString(),
            Payload = payload,
            OccurredAtUtc = occurredAtUtc
        });
    }

    private static TransactionResponse ToResponse(TransactionRecord transaction)
        => new(transaction.Id, transaction.SyncId, transaction.Version, transaction.Date, transaction.AccountId, transaction.CategoryId,
            transaction.Payee, transaction.Memo, transaction.Amount, transaction.CreatedAtUtc, transaction.UpdatedAtUtc);
}
