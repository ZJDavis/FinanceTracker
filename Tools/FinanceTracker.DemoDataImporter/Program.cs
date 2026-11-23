using System.Globalization;
using System.Text.Json;
using FinanceTracker.Data;
using Microsoft.Data.Sqlite;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: FinanceTracker.DemoDataImporter <dataset.json> [sqlite-database-path]");
    return 2;
}

var datasetPath = Path.GetFullPath(args[0]);
var databasePath = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FinanceTracker", "finance.db");

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine($"Dataset file was not found: {datasetPath}");
    return 2;
}

var transactions = JsonSerializer.Deserialize<List<DemoTransaction>>(await File.ReadAllTextAsync(datasetPath))
    ?? throw new InvalidDataException("The dataset file did not contain a transaction list.");

// Run the normal schema upgrades before inserting business records.
new SqliteFinanceStore(databasePath).Initialize();

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true,
    Pooling = false
}.ToString();

await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
using var databaseTransaction = connection.BeginTransaction();

var accountId = await FindOrCreateAccountAsync(connection, databaseTransaction, "ZD Enterprises Operating");
var categoryIds = new Dictionary<string, int>(StringComparer.Ordinal);
var insertedCount = 0;
var existingCount = 0;

foreach (var transaction in transactions)
{
    if (!Guid.TryParse(transaction.SyncId, out var syncId))
        throw new InvalidDataException($"Transaction '{transaction.Payee}' has an invalid SyncId.");

    var categoryKind = transaction.Amount >= 0 ? "Income" : "Expense";
    if (!categoryIds.TryGetValue(transaction.CategoryName, out var categoryId))
    {
        categoryId = await FindOrCreateCategoryAsync(connection, databaseTransaction, transaction.CategoryName, categoryKind);
        categoryIds.Add(transaction.CategoryName, categoryId);
    }

    var date = DateOnly.ParseExact(transaction.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var amount = decimal.Parse(transaction.Amount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    if (await ExistingTransactionMatchesAsync(connection, databaseTransaction, syncId, date, accountId, categoryId,
            transaction.Payee, transaction.Memo, amount))
    {
        existingCount++;
        continue;
    }

    if (await TransactionSyncIdExistsAsync(connection, databaseTransaction, syncId))
        throw new InvalidDataException($"SyncId {syncId:D} already belongs to a different transaction. No import changes were committed.");

    await InsertTransactionAsync(connection, databaseTransaction, syncId, date, accountId, categoryId,
        transaction.Payee, transaction.Memo, amount);
    insertedCount++;
}

databaseTransaction.Commit();
Console.WriteLine($"Imported {insertedCount} transactions; {existingCount} matching records were already present.");
Console.WriteLine($"SQLite database: {databasePath}");
return 0;

static async Task<int> FindOrCreateAccountAsync(SqliteConnection connection, SqliteTransaction transaction, string name)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT Id FROM Accounts WHERE Name=$name LIMIT 1;";
    command.Parameters.AddWithValue("$name", name);
    var existingId = await command.ExecuteScalarAsync();
    if (existingId is not null) return Convert.ToInt32(existingId, CultureInfo.InvariantCulture);

    command.CommandText = "INSERT INTO Accounts(Name,Type,OpeningBalance) VALUES($name,'Checking','0') RETURNING Id;";
    return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

static async Task<int> FindOrCreateCategoryAsync(SqliteConnection connection, SqliteTransaction transaction, string name, string kind)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT Id FROM Categories WHERE Name=$name AND Kind=$kind LIMIT 1;";
    command.Parameters.AddWithValue("$name", name);
    command.Parameters.AddWithValue("$kind", kind);
    var existingId = await command.ExecuteScalarAsync();
    if (existingId is not null) return Convert.ToInt32(existingId, CultureInfo.InvariantCulture);

    command.CommandText = "INSERT INTO Categories(Name,Kind) VALUES($name,$kind) RETURNING Id;";
    return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

static async Task<bool> ExistingTransactionMatchesAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    Guid syncId,
    DateOnly date,
    int accountId,
    int categoryId,
    string payee,
    string memo,
    decimal amount)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT Date,AccountId,CategoryId,Payee,Memo,Amount FROM Transactions WHERE SyncId=$syncId;";
    command.Parameters.AddWithValue("$syncId", syncId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return false;

    return reader.GetString(0) == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        && reader.GetInt32(1) == accountId
        && reader.GetInt32(2) == categoryId
        && reader.GetString(3) == payee
        && reader.GetString(4) == memo
        && decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture) == amount;
}

static async Task<bool> TransactionSyncIdExistsAsync(SqliteConnection connection, SqliteTransaction transaction, Guid syncId)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT EXISTS(SELECT 1 FROM Transactions WHERE SyncId=$syncId);";
    command.Parameters.AddWithValue("$syncId", syncId.ToString("D"));
    return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
}

static async Task InsertTransactionAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    Guid syncId,
    DateOnly date,
    int accountId,
    int categoryId,
    string payee,
    string memo,
    decimal amount)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
        INSERT INTO Transactions(SyncId,Date,AccountId,CategoryId,Payee,Memo,Amount,Version)
        VALUES($syncId,$date,$accountId,$categoryId,$payee,$memo,$amount,1);
        """;
    command.Parameters.AddWithValue("$syncId", syncId.ToString("D"));
    command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("$accountId", accountId);
    command.Parameters.AddWithValue("$categoryId", categoryId);
    command.Parameters.AddWithValue("$payee", payee);
    command.Parameters.AddWithValue("$memo", memo);
    command.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync();
}

internal sealed record DemoTransaction(
    string Date,
    string AccountName,
    string CategoryName,
    string Payee,
    string Memo,
    decimal Amount,
    string SyncId);
