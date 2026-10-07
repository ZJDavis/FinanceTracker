using System.IO;
using FinanceTracker.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed partial class SqliteFinanceStoreTests
{
    [Theory]
    [InlineData("'); DROP TABLE Transactions; --")]
    [InlineData("'; DELETE FROM Accounts; --")]
    [InlineData("' OR 1=1 --")]
    [InlineData("\"; UPDATE Transactions SET Amount='999999'; --")]
    [InlineData("$amount @id :payee ? /* comment */ SELECT * FROM Categories")]
    [InlineData("O'Brien\nтест 💰; --")]
    public void SqlPayloadsRemainLiteralOnInsertAndUpdate(string payload)
    {
        var store = CreateStore();
        var sentinel = Item(); sentinel.Payee = "Untouched"; sentinel.Id = store.SaveTransaction(sentinel);
        var target = Item(); target.Payee = payload; target.Memo = payload; target.Id = store.SaveTransaction(target);
        var inserted = store.LoadTransactions().Single(t => t.Id == target.Id);
        Assert.Equal(payload, inserted.Payee); Assert.Equal(payload, inserted.Memo);
        target.Payee = payload + " updated"; target.Memo = payload + " updated"; store.SaveTransaction(target);
        var rows = CreateStore().LoadTransactions();
        Assert.Equal(2, rows.Count);
        var updated = rows.Single(t => t.Id == target.Id);
        Assert.Equal(target.Payee, updated.Payee); Assert.Equal(target.Memo, updated.Memo);
        Assert.Equal(target.Amount, updated.Amount);
        var untouched = rows.Single(t => t.Id == sentinel.Id);
        Assert.Equal(sentinel.Payee, untouched.Payee); Assert.Equal(sentinel.Amount, untouched.Amount);
        Assert.Equal(2, store.LoadAccounts().Count); Assert.Equal(4, store.LoadCategories().Count);
        store.DeleteTransaction(target.Id);
        Assert.Equal(sentinel.Id, Assert.Single(store.LoadTransactions()).Id);
    }

    [Fact]
    public void ConcurrentEditIsRejectedWithoutOverwritingFirstSave()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        var first = Assert.Single(store.LoadTransactions()); var stale = Assert.Single(CreateStore().LoadTransactions());
        first.Payee = "First writer"; store.SaveTransaction(first);
        stale.Payee = "Stale writer";
        Assert.Throws<InvalidOperationException>(() => store.SaveTransaction(stale));
        var actual = Assert.Single(store.LoadTransactions()); Assert.Equal("First writer", actual.Payee);
        Assert.Equal(2, actual.Version); Assert.Equal(1, stale.Version);
        Assert.Throws<InvalidOperationException>(() => store.DeleteTransaction(stale.Id, stale.Version));
        Assert.Equal("First writer", Assert.Single(store.LoadTransactions()).Payee);
    }

    [Fact]
    public void ConcurrentDeletionDoesNotRecreateMissingRow()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        store.DeleteTransaction(item.Id);
        Assert.Throws<InvalidOperationException>(() => store.SaveTransaction(item));
        Assert.Throws<InvalidOperationException>(() => store.DeleteTransaction(item.Id));
        Assert.Empty(store.LoadTransactions());
    }

    [Fact]
    public void LockedDatabaseRejectsWriteThenAllowsRetry()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        var impatient = new FinanceTracker.Data.SqliteFinanceStore(DatabasePath, timeoutSeconds: 1);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE Transactions SET Memo=Memo"; command.ExecuteNonQuery();
        item.Payee = "Retry";
        Assert.Equal(5, Assert.Throws<SqliteException>(() => impatient.SaveTransaction(item)).SqliteErrorCode);
        Assert.Equal(5, Assert.Throws<SqliteException>(() => impatient.DeleteTransaction(item.Id)).SqliteErrorCode);
        transaction.Rollback();
        Assert.Equal(Item().Payee, Assert.Single(store.LoadTransactions()).Payee);
        impatient.SaveTransaction(item); Assert.Equal("Retry", Assert.Single(store.LoadTransactions()).Payee);
    }

    [Fact]
    public void VersionOneMigrationPreservesExistingRows()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE Transactions DROP COLUMN Version; PRAGMA user_version=1"; command.ExecuteNonQuery();
        }
        var migrated = Assert.Single(CreateStore().LoadTransactions());
        Assert.Equal(item.Id, migrated.Id); Assert.Equal(item.Amount, migrated.Amount); Assert.Equal(item.Payee, migrated.Payee);
        Assert.Equal(1, migrated.Version); migrated.Memo = "After migration"; store.SaveTransaction(migrated);
        Assert.Equal("After migration", Assert.Single(CreateStore().LoadTransactions()).Memo);
    }

    [Fact]
    public void CorruptDatabaseIsRejectedWithoutReplacingItsBytes()
    {
        Directory.CreateDirectory(directory); var bytes = System.Text.Encoding.UTF8.GetBytes("This is not a SQLite database.");
        File.WriteAllBytes(DatabasePath, bytes);
        Assert.Equal(26, Assert.Throws<SqliteException>(() => CreateStore()).SqliteErrorCode);
        Assert.Equal(bytes, File.ReadAllBytes(DatabasePath));
    }

    [Fact]
    public void LongUnicodeTextRoundTripsWithoutTruncation()
    {
        var store = CreateStore(); var item = Item(); item.Payee = new string('Ж', 10000); item.Memo = new string('字', 100000) + "\n💰";
        item.Id = store.SaveTransaction(item); var actual = Assert.Single(CreateStore().LoadTransactions());
        Assert.Equal(item.Payee, actual.Payee); Assert.Equal(item.Memo, actual.Memo);
    }

    [Fact]
    public void ReadOnlyDatabaseDoesNotLoseData()
    {
        var store = CreateStore(); var item = Item(); item.Id = store.SaveTransaction(item);
        File.SetAttributes(DatabasePath, File.GetAttributes(DatabasePath) | FileAttributes.ReadOnly);
        try
        {
            item.Payee = "Must not persist";
            Assert.Equal(8, Assert.Throws<SqliteException>(() => store.SaveTransaction(item)).SqliteErrorCode);
            Assert.Equal(Item().Payee, Assert.Single(store.LoadTransactions()).Payee);
        }
        finally { File.SetAttributes(DatabasePath, FileAttributes.Normal); }
    }
}
