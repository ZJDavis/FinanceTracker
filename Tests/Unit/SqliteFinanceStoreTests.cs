using System.IO;
using FinanceTracker.Core.Models;
using FinanceTracker.Data;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed partial class SqliteFinanceStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FinanceTrackerTests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "finance.db");
    private SqliteFinanceStore CreateStore() { var store = new SqliteFinanceStore(DatabasePath); store.Initialize(); return store; }
    private static Transaction Item() => new() { Date = new DateOnly(2024, 2, 29), AccountId = 1, CategoryId = 1, Payee = "O'Brien; DROP TABLE Accounts; --", Memo = "Unicode: тест", Amount = -123456789.123456789m };

    [Fact]
    public void InitializationIsIdempotentAndDoesNotSeedTransactions()
    {
        var store = CreateStore(); store.Initialize();
        Assert.Equal(2, store.LoadAccounts().Count); Assert.Equal(4, store.LoadCategories().Count);
        Assert.Empty(store.LoadTransactions()); Assert.All(store.LoadAccounts(), a => Assert.Equal(0m, a.OpeningBalance));
    }
    [Fact]
    public void SavedDataSurvivesReopeningWithExactMoneyAndText()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        var reopened = CreateStore(); var actual = Assert.Single(reopened.LoadTransactions());
        Assert.Equal(item.Id, actual.Id); Assert.Equal(item.Amount, actual.Amount); Assert.Equal(item.Date, actual.Date);
        Assert.Equal(item.Payee, actual.Payee); Assert.Equal(item.Memo, actual.Memo); Assert.Equal(2, reopened.LoadAccounts().Count);
    }
    [Fact]
    public void UpdateAndDeletePersistWithoutDuplicateRows()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        item.Amount = 25.75m; item.Payee = "Updated"; item.Memo = "Updated memo"; item.AccountId = 2; item.CategoryId = 3; item.Date = new DateOnly(2025, 12, 31); Assert.Equal(item.Id, store.SaveTransaction(item));
        var updated = Assert.Single(CreateStore().LoadTransactions());
        Assert.Equal(item.Amount, updated.Amount); Assert.Equal(item.Payee, updated.Payee); Assert.Equal(item.Memo, updated.Memo);
        Assert.Equal(item.AccountId, updated.AccountId); Assert.Equal(item.CategoryId, updated.CategoryId); Assert.Equal(item.Date, updated.Date);
        store.DeleteTransaction(item.Id); Assert.Empty(CreateStore().LoadTransactions());
    }
    [Fact]
    public void ForeignKeysRejectUnknownAccountAndCategory()
    {
        var store = CreateStore(); var item = Item(); item.AccountId = 999;
        Assert.Equal(787, Assert.Throws<SqliteException>(() => store.SaveTransaction(item)).SqliteExtendedErrorCode);
        item.AccountId = 1; item.CategoryId = 999;
        Assert.Equal(787, Assert.Throws<SqliteException>(() => store.SaveTransaction(item)).SqliteExtendedErrorCode); Assert.Empty(store.LoadTransactions());
    }
    [Fact]
    public void MissingUpdateDoesNotInsertOrReplaceExistingData()
    {
        var store = CreateStore(); var existing = Item(); existing.Id = store.SaveTransaction(existing); var item = Item(); item.Id = 999;
        Assert.Throws<InvalidOperationException>(() => store.SaveTransaction(item)); Assert.Equal(existing.Payee, Assert.Single(store.LoadTransactions()).Payee);
    }
    [Fact]
    public void DeletedIdsAreNotReused()
    {
        var store = CreateStore(); var item = Item(); var first = store.SaveTransaction(item);
        store.DeleteTransaction(first); Assert.True(store.SaveTransaction(item) > first);
    }
    [Fact]
    public void NewerSchemaIsRejectedWithoutChangingVersion()
    {
        _ = CreateStore(); using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=3"; command.ExecuteNonQuery();
        Assert.Throws<InvalidOperationException>(() => CreateStore()); command.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, command.ExecuteScalar());
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
