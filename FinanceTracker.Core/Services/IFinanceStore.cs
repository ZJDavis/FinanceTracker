using FinanceTracker.Core.Models;
namespace FinanceTracker.Core.Services;
public interface IFinanceStore
{
    void Initialize();
    IReadOnlyList<Account> LoadAccounts();
    IReadOnlyList<Category> LoadCategories();
    IReadOnlyList<Transaction> LoadTransactions();
    int SaveTransaction(Transaction transaction);
    void DeleteTransaction(int id, int? version = null);
}
