using System.Diagnostics;
using System.Globalization;
using System.IO;
using FinanceTracker.Core.Models;
using FinanceTracker.Data;
using FinanceTracker.Wpf.ViewModels;

internal static class EdgeCases
{
    public static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("Cancel draft with no visible rows", () =>
        {
            var vm = new TransactionsViewModel(); vm.SearchText = "No matches";
            vm.AddCommand.Execute(null); vm.Editor.Payee = "Unsaved"; vm.CancelCommand.Execute(null);
            Check(vm.Transactions.Count == 3 && vm.SelectedTransaction is null, "Cancel must not insert or select hidden rows");
            Check(!vm.SaveCommand.CanExecute(null) && !vm.CancelCommand.CanExecute(null), "No target must disable commands");
        }),
        ("Leap-day and year-boundary month filtering", () =>
        {
            foreach (var month in new[] { new DateOnly(2024, 2, 1), new DateOnly(2024, 12, 1), new DateOnly(2025, 1, 1) })
            {
                var vm = new TransactionsViewModel(); vm.Transactions.Clear();
                var end = month.AddMonths(1).AddDays(-1);
                foreach (var day in new[] { month.AddDays(-1), month, end, end.AddDays(1) })
                    vm.Transactions.Add(new Transaction { AccountId = 1, Date = day, Payee = "Date", Amount = 1m });
                vm.SelectedMonth = month.ToDateTime(TimeOnly.MinValue);
                Check(vm.Total == 2m && vm.TransactionsView.Cast<Transaction>().Count() == 2, "Only selected month's first and last days belong");
            }
        }),
        ("Total overflow is reported and can recover", () =>
        {
            var vm = new TransactionsViewModel(); vm.Transactions.Clear();
            vm.Transactions.Add(new Transaction { AccountId = 1, Payee = "Large", Amount = decimal.MaxValue });
            vm.Transactions.Add(new Transaction { AccountId = 1, Payee = "Large", Amount = 1m });
            Check(vm.Total is null && vm.TotalWarning.Length > 0, "Overflow must produce a warning rather than crash or wrap");
            vm.Transactions.RemoveAt(1);
            Check(vm.Total == decimal.MaxValue && vm.TotalWarning == "", "Total must recover after overflow is removed");
        }),
        ("Reopening with only historic transactions selects nothing", () => WithDatabase(path =>
        {
            var store = new SqliteFinanceStore(path); store.Initialize();
            store.SaveTransaction(new Transaction { AccountId = 1, CategoryId = 1, Date = new DateOnly(2000, 1, 1), Payee = "Historic", Amount = -10m });
            var vm = new TransactionsViewModel(store);
            Check(vm.SelectedTransaction is null && vm.Total == 0m && !vm.SaveCommand.CanExecute(null), "Historic row must not be selected outside current filters");
        })),
        ("Moving to another account and month persists and updates total", () => WithDatabase(path =>
        {
            var store = new SqliteFinanceStore(path); var vm = new TransactionsViewModel(store);
            vm.AddCommand.Execute(null); vm.Editor.Payee = "Move"; vm.Editor.AmountText = "10"; vm.SaveCommand.Execute(null);
            vm.Editor.AccountId = 2; vm.Editor.Date = vm.SelectedMonth.AddMonths(1); vm.SaveCommand.Execute(null);
            Check(vm.Total == 0m && vm.SelectedTransaction is null, "Moved row must disappear from account/month view");
            var saved = store.LoadTransactions().Single();
            Check(saved.AccountId == 2 && saved.Date == DateOnly.FromDateTime(vm.SelectedMonth.AddMonths(1)), "Both changes must persist");
        })),
        ("Stale save preserves draft and first writer's data", () => WithDatabase(path =>
        {
            var store = new SqliteFinanceStore(path); var vm = new TransactionsViewModel(store);
            vm.AddCommand.Execute(null); vm.Editor.Payee = "Original"; vm.Editor.AmountText = "10"; vm.SaveCommand.Execute(null);
            var concurrent = store.LoadTransactions().Single(); concurrent.Payee = "Other instance"; store.SaveTransaction(concurrent);
            vm.Editor.Payee = "My draft"; vm.SaveCommand.Execute(null);
            Check(vm.ErrorMessage.StartsWith("Save failed") && vm.Editor.Payee == "My draft", "Conflict should preserve draft and report error");
            Check(store.LoadTransactions().Single().Payee == "Other instance", "Stale save must not overwrite another instance");
        })),
        ("Persistence survives separate processes", () => WithDatabase(path =>
        {
            RunProbe("--write-probe", path); RunProbe("--read-probe", path);
        }))
    };

    public static int Probe(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected probe mode and database path.");
            var store = new SqliteFinanceStore(args[1]); store.Initialize();
            if (args[0] == "--write-probe")
                store.SaveTransaction(new Transaction { AccountId = 1, CategoryId = 1, Date = new DateOnly(2024, 2, 29), Payee = "Process round trip", Memo = "тест", Amount = -12.3456789m });
            else if (args[0] == "--read-probe")
            {
                var item = store.LoadTransactions().Single();
                Check(item.Payee == "Process round trip" && item.Memo == "тест" && item.Amount == -12.3456789m && item.Date == new DateOnly(2024, 2, 29), "Separate process must reload exact persisted values");
            }
            else throw new ArgumentException("Unknown probe mode.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void RunProbe(string mode, string path)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(EdgeCases).Assembly.Location);
        info.ArgumentList.Add(mode); info.ArgumentList.Add(path);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch probe.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("Probe did not exit."); }
        Check(process.ExitCode == 0, "Separate process failed: " + errors.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }
    private static void WithDatabase(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FinanceTrackerRegression", Guid.NewGuid().ToString("N"));
        try { action(Path.Combine(folder, "finance.db")); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
}
