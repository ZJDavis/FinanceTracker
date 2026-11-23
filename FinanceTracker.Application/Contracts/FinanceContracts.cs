namespace FinanceTracker.Application.Contracts;

public sealed record AccountResponse(int Id, string Name, string Type, decimal OpeningBalance, bool IsActive);
public sealed record CategoryResponse(int Id, string Name, string Kind, bool IsActive);

public sealed record TransactionRequest(DateOnly Date, int AccountId, int CategoryId, string Payee, string? Memo, decimal Amount, Guid? SyncId = null);

public sealed record TransactionResponse(int Id, Guid SyncId, int Version, DateOnly Date, int AccountId, int CategoryId, string Payee, string Memo, decimal Amount, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record TransactionQuery(int? AccountId, int? CategoryId, DateOnly? From, DateOnly? To, string? Search, string Sort, string Direction, int Page, int PageSize);

public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record ActivityResponse(DateOnly From, DateOnly To, int? AccountId, decimal IncomeTotal, decimal ExpenseTotal, decimal NetActivity, int TransactionCount);

public sealed record EventReceiptResponse(string EventId, string EventType, DateTimeOffset OccurredAtUtc, DateTimeOffset ReceivedAtUtc);

public sealed class FinanceValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class FinanceNotFoundException(string message) : Exception(message);
public sealed class FinanceConcurrencyException(string message) : Exception(message);

public interface IFinanceService
{
    Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<AccountResponse> GetAccountAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<CategoryResponse> GetCategoryAsync(int id, CancellationToken cancellationToken);
    Task<PageResponse<TransactionResponse>> GetTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken);
    Task<TransactionResponse> GetTransactionAsync(int id, CancellationToken cancellationToken);
    Task<TransactionResponse> CreateTransactionAsync(TransactionRequest request, CancellationToken cancellationToken);
    Task<TransactionResponse> UpdateTransactionAsync(int id, int expectedVersion, TransactionRequest request, CancellationToken cancellationToken);
    Task DeleteTransactionAsync(int id, int expectedVersion, CancellationToken cancellationToken);
    Task<ActivityResponse> GetActivityAsync(DateOnly from, DateOnly to, int? accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventReceiptResponse>> GetRecentEventReceiptsAsync(int limit, CancellationToken cancellationToken);
}
