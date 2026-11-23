using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceTracker.Data.Postgres;

public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FINANCETRACKER_CONNECTION_STRING")
            ?? "Host=localhost;Database=financetracker;Username=finance_demo;Password=design-time-only";
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        return new FinanceDbContext(options);
    }
}
