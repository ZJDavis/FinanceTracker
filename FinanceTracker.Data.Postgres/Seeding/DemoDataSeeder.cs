using FinanceTracker.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Data.Postgres.Seeding;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(FinanceDbContext database, CancellationToken cancellationToken = default)
    {
        var accountNames = new[] { "Demo Checking", "Demo Credit Card", "ZD Enterprises Operating" };
        foreach (var accountName in accountNames)
        {
            var nameKey = NormalizeName(accountName);
            if (!await database.Accounts.AnyAsync(account => account.NameKey == nameKey, cancellationToken))
            {
                database.Accounts.Add(new AccountRecord
                {
                    Name = accountName,
                    NameKey = nameKey,
                    Type = accountName.EndsWith("Card", StringComparison.Ordinal) ? "Credit Card" : "Checking",
                    OpeningBalance = 1000m,
                    IsActive = true
                });
            }
        }

        var categoryDefinitions = new (string Name, string Kind)[]
        {
            ("Demo Groceries", "Expense"),
            ("Demo Rent", "Expense"),
            ("Demo Salary", "Income"),
            ("Demo Refunds", "Income"),
            ("Consulting Revenue", "Income"),
            ("Payroll", "Expense"),
            ("Payroll Taxes", "Expense"),
            ("Office Rent", "Expense"),
            ("Software & Licenses", "Expense"),
            ("Cloud Hosting", "Expense"),
            ("Internet & Phone", "Expense"),
            ("Insurance", "Expense"),
            ("Marketing", "Expense"),
            ("Utilities", "Expense"),
            ("Accounting & Legal", "Expense"),
            ("Office Supplies", "Expense"),
            ("Bank Fees", "Expense"),
            ("Travel", "Expense"),
            ("Training", "Expense"),
            ("Equipment", "Expense"),
            ("Security & Compliance", "Expense"),
            ("Repairs & Maintenance", "Expense"),
            ("Taxes & Licenses", "Expense")
        };
        foreach (var (name, kind) in categoryDefinitions)
        {
            var nameKey = NormalizeName(name);
            if (!await database.Categories.AnyAsync(category => category.NameKey == nameKey, cancellationToken))
            {
                database.Categories.Add(new CategoryRecord { Name = name, NameKey = nameKey, Kind = kind, IsActive = true });
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        if (await database.Transactions.AnyAsync(transaction => transaction.Payee.StartsWith("[DEMO] "), cancellationToken))
            return;

        var checkingId = await database.Accounts.Where(account => account.NameKey == NormalizeName("Demo Checking"))
            .Select(account => account.Id).SingleAsync(cancellationToken);
        var cardId = await database.Accounts.Where(account => account.NameKey == NormalizeName("Demo Credit Card"))
            .Select(account => account.Id).SingleAsync(cancellationToken);
        var groceriesId = await GetCategoryIdAsync(database, "Demo Groceries", cancellationToken);
        var rentId = await GetCategoryIdAsync(database, "Demo Rent", cancellationToken);
        var salaryId = await GetCategoryIdAsync(database, "Demo Salary", cancellationToken);
        var refundsId = await GetCategoryIdAsync(database, "Demo Refunds", cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var sampleTransactions = new (DateOnly Date, int AccountId, int CategoryId, string Payee, string Memo, decimal Amount)[]
        {
            (new(2026, 1, 1), checkingId, salaryId, "[DEMO] January pay", "Synthetic salary", 3200m),
            (new(2026, 1, 5), checkingId, groceriesId, "[DEMO] Market", "Synthetic groceries", -74.28m),
            (new(2026, 1, 5), checkingId, groceriesId, "[DEMO] Pharmacy", "Same-date stable sort example", -74.28m),
            (new(2026, 1, 31), checkingId, rentId, "[DEMO] Rent", "Month boundary example", -1200m),
            (new(2026, 2, 1), checkingId, salaryId, "[DEMO] February pay", "Synthetic salary", 3200m),
            (new(2026, 2, 14), cardId, groceriesId, "[DEMO] Cafe", "Synthetic card purchase", -23.5m),
            (new(2026, 2, 18), checkingId, refundsId, "[DEMO] Returned item", "Synthetic refund", 12.99m),
            (new(2026, 2, 28), checkingId, rentId, "[DEMO] Rent", "Month boundary example", -1200m),
            (new(2026, 3, 1), checkingId, salaryId, "[DEMO] March pay", "Synthetic salary", 3200m),
            (new(2026, 3, 31), cardId, groceriesId, "[DEMO] Groceries", "Month boundary example", -105.67m)
        };

        foreach (var sample in sampleTransactions)
        {
            database.Transactions.Add(new TransactionRecord
            {
                Date = sample.Date,
                AccountId = sample.AccountId,
                CategoryId = sample.CategoryId,
                Payee = sample.Payee,
                Memo = sample.Memo,
                Amount = sample.Amount,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static Task<int> GetCategoryIdAsync(FinanceDbContext database, string name, CancellationToken cancellationToken)
        => database.Categories.Where(category => category.NameKey == NormalizeName(name))
            .Select(category => category.Id).SingleAsync(cancellationToken);

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();
}
