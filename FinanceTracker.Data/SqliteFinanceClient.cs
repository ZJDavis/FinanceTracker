using FinanceTracker.Application.Clients;
using FinanceTracker.Application.Contracts;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;

namespace FinanceTracker.Data;

/// <summary>
/// Adapts the existing synchronous SQLite store to the asynchronous desktop boundary.
/// SQLite work runs off the WPF dispatcher; the database format and user data stay unchanged.
/// </summary>
public sealed class SqliteFinanceClient(IFinanceStore store) : IFinanceClient
{
    public string ModeName => "SQLite (local)";

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.Run(store.Initialize, cancellationToken);

    public Task<IReadOnlyList<Account>> LoadAccountsAsync(CancellationToken cancellationToken = default)
        => Task.Run(store.LoadAccounts, cancellationToken);

    public Task<IReadOnlyList<Category>> LoadCategoriesAsync(CancellationToken cancellationToken = default)
        => Task.Run(store.LoadCategories, cancellationToken);

    public Task<PageResponse<Transaction>> GetTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            IEnumerable<Transaction> transactions = store.LoadTransactions();
            if (query.AccountId is int accountId) transactions = transactions.Where(transaction => transaction.AccountId == accountId);
            if (query.CategoryId is int categoryId) transactions = transactions.Where(transaction => transaction.CategoryId == categoryId);
            if (query.From is DateOnly from) transactions = transactions.Where(transaction => transaction.Date >= from);
            if (query.To is DateOnly to) transactions = transactions.Where(transaction => transaction.Date <= to);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                transactions = transactions.Where(transaction =>
                    transaction.Payee.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    transaction.Memo.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = transactions.Count();
            IOrderedEnumerable<Transaction> ordered = query.Sort switch
            {
                "amount" => query.Direction == "asc" ? transactions.OrderBy(item => item.Amount).ThenBy(item => item.Id) : transactions.OrderByDescending(item => item.Amount).ThenByDescending(item => item.Id),
                "payee" => query.Direction == "asc" ? transactions.OrderBy(item => item.Payee).ThenBy(item => item.Id) : transactions.OrderByDescending(item => item.Payee).ThenByDescending(item => item.Id),
                _ => query.Direction == "asc" ? transactions.OrderBy(item => item.Date).ThenBy(item => item.Id) : transactions.OrderByDescending(item => item.Date).ThenByDescending(item => item.Id)
            };
            var pageItems = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
            return new PageResponse<Transaction>(pageItems, query.Page, query.PageSize, totalCount, totalPages);
        }, cancellationToken);

    public Task<Transaction> GetTransactionAsync(int id, CancellationToken cancellationToken = default)
        => Task.Run(() => store.LoadTransactions().SingleOrDefault(transaction => transaction.Id == id)
            ?? throw new FinanceNotFoundException($"Transaction {id} was not found in SQLite."), cancellationToken);

    public Task<ActivityResponse> GetActivityAsync(DateOnly from, DateOnly to, int? accountId, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var matchingTransactions = store.LoadTransactions()
                .Where(transaction => transaction.Date >= from && transaction.Date <= to);
            if (accountId is int selectedAccountId)
                matchingTransactions = matchingTransactions.Where(transaction => transaction.AccountId == selectedAccountId);

            var matching = matchingTransactions.ToArray();
            var income = matching.Where(transaction => transaction.Amount > 0).Sum(transaction => transaction.Amount);
            var expenseMagnitude = -matching.Where(transaction => transaction.Amount < 0).Sum(transaction => transaction.Amount);
            return new ActivityResponse(from, to, accountId, income, expenseMagnitude, matching.Sum(transaction => transaction.Amount), matching.Length);
        }, cancellationToken);

    public Task<Transaction> SaveTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            transaction.Id = store.SaveTransaction(transaction);
            return transaction;
        }, cancellationToken);

    public Task DeleteTransactionAsync(int id, int? version = null, CancellationToken cancellationToken = default)
        => Task.Run(() => store.DeleteTransaction(id, version), cancellationToken);
}
