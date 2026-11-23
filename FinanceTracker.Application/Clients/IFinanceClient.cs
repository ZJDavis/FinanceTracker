using FinanceTracker.Application.Contracts;
using FinanceTracker.Core.Models;
using System.Net;

namespace FinanceTracker.Application.Clients;

/// <summary>
/// The desktop app uses this async boundary for both SQLite and HTTP data modes.
/// </summary>
public interface IFinanceClient
{
    string ModeName { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> LoadAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> LoadCategoriesAsync(CancellationToken cancellationToken = default);
    Task<PageResponse<Transaction>> GetTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default);
    Task<Transaction> GetTransactionAsync(int id, CancellationToken cancellationToken = default);
    Task<ActivityResponse> GetActivityAsync(DateOnly from, DateOnly to, int? accountId, CancellationToken cancellationToken = default);
    Task<Transaction> SaveTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task DeleteTransactionAsync(int id, int? version = null, CancellationToken cancellationToken = default);
}

public sealed class FinanceApiException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
