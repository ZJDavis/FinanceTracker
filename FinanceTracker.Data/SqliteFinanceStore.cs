using System.Globalization;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using Microsoft.Data.Sqlite;
namespace FinanceTracker.Data;

public sealed class SqliteFinanceStore : IFinanceStore
{
    private readonly string connectionString;
    public SqliteFinanceStore(string databasePath, int timeoutSeconds = 5)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false, DefaultTimeout = timeoutSeconds }.ToString();
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try { connection.Open(); }
        catch { connection.Dispose(); throw; }
        return connection;
    }
    public void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version > 4) throw new InvalidOperationException("This database requires a newer FinanceTracker version.");
        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE Accounts(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Type TEXT NOT NULL, OpeningBalance TEXT NOT NULL);
                CREATE TABLE Categories(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Kind TEXT NOT NULL);
                CREATE TABLE Transactions(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT NOT NULL, AccountId INTEGER NOT NULL REFERENCES Accounts(Id), CategoryId INTEGER NOT NULL REFERENCES Categories(Id), Payee TEXT NOT NULL CHECK(length(trim(Payee)) > 0), Memo TEXT NOT NULL, Amount TEXT NOT NULL);
                CREATE INDEX IX_Transactions_AccountDate ON Transactions(AccountId, Date);
                INSERT INTO Accounts(Name,Type,OpeningBalance) VALUES('Checking','Checking','0'),('Credit Card','Credit','0');
                INSERT INTO Categories(Name,Kind) VALUES('Groceries','Expense'),('Rent','Expense'),('Salary','Income'),('Uncategorized','Expense');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 2)
        {
            command.CommandText = "ALTER TABLE Transactions ADD COLUMN Version INTEGER NOT NULL DEFAULT 1; PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }
        if (version < 3)
        {
            // Existing rows receive one permanent identity; new rows get theirs in the application.
            command.CommandText = "ALTER TABLE Transactions ADD COLUMN SyncId TEXT NOT NULL DEFAULT ''; UPDATE Transactions SET SyncId=lower(hex(randomblob(16))) WHERE SyncId=''; CREATE UNIQUE INDEX IX_Transactions_SyncId ON Transactions(SyncId); PRAGMA user_version=3;";
            command.ExecuteNonQuery();
        }
        if (version < 4)
        {
            // Keep the synthetic business ledger ready to import in existing local databases.
            command.CommandText = """
                INSERT INTO Accounts(Name,Type,OpeningBalance)
                    SELECT 'ZD Enterprises Operating','Checking','0'
                    WHERE NOT EXISTS (SELECT 1 FROM Accounts WHERE Name='ZD Enterprises Operating');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Consulting Revenue','Income' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Consulting Revenue');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Payroll','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Payroll');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Payroll Taxes','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Payroll Taxes');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Office Rent','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Office Rent');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Software & Licenses','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Software & Licenses');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Cloud Hosting','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Cloud Hosting');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Internet & Phone','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Internet & Phone');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Insurance','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Insurance');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Marketing','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Marketing');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Utilities','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Utilities');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Accounting & Legal','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Accounting & Legal');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Office Supplies','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Office Supplies');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Bank Fees','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Bank Fees');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Travel','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Travel');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Training','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Training');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Equipment','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Equipment');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Security & Compliance','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Security & Compliance');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Repairs & Maintenance','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Repairs & Maintenance');
                INSERT INTO Categories(Name,Kind)
                    SELECT 'Taxes & Licenses','Expense' WHERE NOT EXISTS (SELECT 1 FROM Categories WHERE Name='Taxes & Licenses');
                PRAGMA user_version=4;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public IReadOnlyList<Account> LoadAccounts()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Type,OpeningBalance FROM Accounts ORDER BY Id";
        using var reader = command.ExecuteReader(); var result = new List<Account>();
        while (reader.Read()) result.Add(new Account { Id = reader.GetInt32(0), Name = reader.GetString(1), Type = reader.GetString(2), OpeningBalance = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture) });
        return result;
    }
    public IReadOnlyList<Category> LoadCategories()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Kind FROM Categories ORDER BY Id";
        using var reader = command.ExecuteReader(); var result = new List<Category>();
        while (reader.Read()) result.Add(new Category { Id = reader.GetInt32(0), Name = reader.GetString(1), Kind = reader.GetString(2) });
        return result;
    }
    public IReadOnlyList<Transaction> LoadTransactions()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,SyncId,Date,AccountId,CategoryId,Payee,Memo,Amount,Version FROM Transactions ORDER BY Date,Id";
        using var reader = command.ExecuteReader(); var result = new List<Transaction>();
        while (reader.Read()) result.Add(new Transaction { Id = reader.GetInt32(0), SyncId = Guid.Parse(reader.GetString(1)), Date = DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture), AccountId = reader.GetInt32(3), CategoryId = reader.GetInt32(4), Payee = reader.GetString(5), Memo = reader.GetString(6), Amount = decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture), Version = reader.GetInt32(8) });
        return result;
    }
    public int SaveTransaction(Transaction item)
    {
        if (item.SyncId == Guid.Empty) item.SyncId = Guid.NewGuid();
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = item.Id == 0
            ? "INSERT INTO Transactions(SyncId,Date,AccountId,CategoryId,Payee,Memo,Amount) VALUES($syncId,$date,$account,$category,$payee,$memo,$amount) RETURNING Id"
            : "UPDATE Transactions SET Date=$date,AccountId=$account,CategoryId=$category,Payee=$payee,Memo=$memo,Amount=$amount,Version=Version+1 WHERE Id=$id AND Version=$version RETURNING Id";
        if (item.Id != 0)
        {
            Bind(command, "$id", SqliteType.Integer, item.Id);
            Bind(command, "$version", SqliteType.Integer, item.Version);
        }
        else
        {
            Bind(command, "$syncId", SqliteType.Text, item.SyncId.ToString("D"));
        }
        Bind(command, "$date", SqliteType.Text, item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Bind(command, "$account", SqliteType.Integer, item.AccountId);
        Bind(command, "$category", SqliteType.Integer, item.CategoryId);
        Bind(command, "$payee", SqliteType.Text, item.Payee);
        Bind(command, "$memo", SqliteType.Text, item.Memo);
        // TEXT preserves decimal precision instead of converting money to binary floating point.
        Bind(command, "$amount", SqliteType.Text, item.Amount.ToString(CultureInfo.InvariantCulture));
        var id = command.ExecuteScalar() ?? throw new InvalidOperationException("The transaction was changed or deleted by another instance. Reload the app before saving.");
        var savedId = Convert.ToInt32(id);
        transaction.Commit();
        item.Version = item.Id == 0 ? 1 : item.Version + 1;
        return savedId;
    }
    public void DeleteTransaction(int id, int? version = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = version is null
            ? "DELETE FROM Transactions WHERE Id=$id"
            : "DELETE FROM Transactions WHERE Id=$id AND Version=$version";
        Bind(command, "$id", SqliteType.Integer, id);
        if (version is not null) Bind(command, "$version", SqliteType.Integer, version.Value);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The transaction was changed or deleted. Reload the app.");
    }

    // SQL syntax and parameter names are fixed by this class. Never interpolate field
    // values into CommandText or attempt to remove SQL keywords from user text.
    private static void Bind(SqliteCommand command, string name, SqliteType type, object value)
        => command.Parameters.Add(name, type).Value = value;
}
