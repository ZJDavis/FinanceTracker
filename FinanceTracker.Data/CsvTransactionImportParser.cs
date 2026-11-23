using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Data;

public sealed record CsvImportPreviewRow(
    int RowNumber,
    Transaction? Transaction,
    string Status,
    string Details,
    bool IsValid,
    bool IsDuplicate)
{
    public bool Include { get; set; } = IsValid && !IsDuplicate;
    public string AccountName { get; init; } = "";
    public string CategoryName { get; init; } = "";
}

/// <summary>
/// Reads the documented FinanceTracker CSV format and checks each row before saving.
/// </summary>
public static class CsvTransactionImportParser
{
    private static readonly string[] RequiredHeaders = ["Date", "AccountName", "CategoryName", "Payee", "Memo", "Amount"];

    public static IReadOnlyList<CsvImportPreviewRow> ReadPreview(
        string csvPath,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Transaction> existingTransactions)
    {
        using var textReader = new StreamReader(csvPath);
        using var csv = new CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = arguments => arguments.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            BadDataFound = null
        });

        if (!csv.Read() || !csv.ReadHeader())
            throw new InvalidDataException("The CSV file is empty or missing its header row.");

        var headers = csv.HeaderRecord ?? [];
        var missingHeaders = RequiredHeaders.Where(required =>
            !headers.Contains(required, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missingHeaders.Length > 0)
            throw new InvalidDataException("The CSV is missing required columns: " + string.Join(", ", missingHeaders));

        var knownTransactions = new HashSet<TransactionFingerprint>(existingTransactions.Select(CreateFingerprint));
        var seenImportedTransactions = new HashSet<TransactionFingerprint>();
        var seenSyncIds = new HashSet<Guid>(existingTransactions.Select(transaction => transaction.SyncId));
        var previewRows = new List<CsvImportPreviewRow>();

        while (csv.Read())
        {
            var rowNumber = csv.Parser.Row;
            try
            {
                previewRows.Add(ReadRow(csv, rowNumber, accounts, categories,
                    knownTransactions, seenImportedTransactions, seenSyncIds));
            }
            catch (CsvHelperException exception)
            {
                previewRows.Add(new CsvImportPreviewRow(rowNumber, null, "Invalid", exception.Message, false, false));
            }
        }

        return previewRows;
    }

    private static CsvImportPreviewRow ReadRow(
        CsvReader csv,
        int rowNumber,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Category> categories,
        HashSet<TransactionFingerprint> knownTransactions,
        HashSet<TransactionFingerprint> seenImportedTransactions,
        HashSet<Guid> seenSyncIds)
    {
        var dateText = csv.GetField("Date")?.Trim();
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Invalid(rowNumber, "Date must use YYYY-MM-DD format.");

        var amountText = csv.GetField("Amount")?.Trim();
        if (!decimal.TryParse(amountText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) || amount == 0m)
            return Invalid(rowNumber, "Amount must be a non-zero decimal using a period as the decimal separator.");

        var accountName = csv.GetField("AccountName")?.Trim() ?? "";
        var matchingAccounts = accounts.Where(account => string.Equals(account.Name, accountName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchingAccounts.Length != 1)
            return Invalid(rowNumber, matchingAccounts.Length == 0
                ? $"Account '{accountName}' does not exist in the selected data source."
                : $"Account '{accountName}' is ambiguous.");

        var categoryName = csv.GetField("CategoryName")?.Trim() ?? "";
        var expectedKind = amount > 0m ? "Income" : "Expense";
        var matchingCategories = categories.Where(category =>
            string.Equals(category.Name, categoryName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(category.Kind, expectedKind, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchingCategories.Length != 1)
            return Invalid(rowNumber, matchingCategories.Length == 0
                ? $"Category '{categoryName}' for {expectedKind.ToLowerInvariant()} is not available."
                : $"Category '{categoryName}' is ambiguous.");

        var payee = csv.GetField("Payee")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(payee)) return Invalid(rowNumber, "Payee is required.");

        var memo = csv.GetField("Memo")?.Trim() ?? "";
        var syncIdText = csv.TryGetField("SyncId", out string? suppliedSyncId) ? suppliedSyncId?.Trim() : null;
        var syncId = Guid.TryParse(syncIdText, out var parsedSyncId) ? parsedSyncId : Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(syncIdText) && !Guid.TryParse(syncIdText, out _))
            return Invalid(rowNumber, "SyncId must be a GUID when the optional column is supplied.");

        var transaction = new Transaction
        {
            SyncId = syncId,
            Date = date,
            AccountId = matchingAccounts[0].Id,
            CategoryId = matchingCategories[0].Id,
            Payee = payee,
            Memo = memo,
            Amount = amount
        };

        var fingerprint = CreateFingerprint(transaction);
        var duplicateByContent = knownTransactions.Contains(fingerprint) || !seenImportedTransactions.Add(fingerprint);
        var duplicateById = !seenSyncIds.Add(syncId);
        var isDuplicate = duplicateByContent || duplicateById;

        return new CsvImportPreviewRow(
            rowNumber,
            transaction,
            isDuplicate ? "Possible duplicate" : "Ready",
            duplicateById ? "This SyncId is already present." : duplicateByContent
                ? "An identical transaction is already in the database or CSV. Review before including it."
                : "Validated and ready to import.",
            true,
            isDuplicate)
        {
            AccountName = matchingAccounts[0].Name,
            CategoryName = matchingCategories[0].Name
        };
    }

    private static CsvImportPreviewRow Invalid(int rowNumber, string details)
        => new(rowNumber, null, "Invalid", details, false, false);

    private static TransactionFingerprint CreateFingerprint(Transaction transaction)
        => new(transaction.Date, transaction.AccountId, transaction.CategoryId,
            transaction.Payee.Trim().ToUpperInvariant(), transaction.Memo.Trim().ToUpperInvariant(), transaction.Amount);

    private readonly record struct TransactionFingerprint(
        DateOnly Date,
        int AccountId,
        int CategoryId,
        string Payee,
        string Memo,
        decimal Amount);
}
