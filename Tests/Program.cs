using System.Globalization;
using FinanceTracker.Core.Models;
using FinanceTracker.Wpf.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) return EdgeCases.Probe(args);
        var tests = new (string Name, Action Run)[]
        {
            ("Database failures preserve displayed data and editor", () =>
            {
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FinanceTrackerRegression", Guid.NewGuid().ToString("N"));
                try
                {
                    var path = System.IO.Path.Combine(folder, "finance.db");
                    var store = new FinanceTracker.Data.SqliteFinanceStore(path);
                    var vm = new TransactionsViewModel(store);
                    vm.AddCommand.Execute(null); vm.Editor.Payee = "Original"; vm.Editor.AmountText = "10"; vm.SaveCommand.Execute(null);
                    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
                    {
                        connection.Open(); using var command = connection.CreateCommand();
                        command.CommandText = "DROP TABLE Transactions"; command.ExecuteNonQuery();
                    }
                    vm.Editor.Payee = "Unsaved edit"; vm.SaveCommand.Execute(null);
                    Check(vm.Transactions.Single().Payee == "Original" && vm.Editor.Payee == "Unsaved edit" && vm.ErrorMessage.StartsWith("Save failed"), "Failed save should retain original and draft");
                    vm.DeleteCommand.Execute(null);
                    Check(vm.Transactions.Count == 1 && vm.ErrorMessage.StartsWith("Delete failed"), "Failed delete should keep displayed row");
                }
                finally { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); }
            }),
            ("SQLite editor survives reopening and draft cancellation", () =>
            {
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FinanceTrackerRegression", Guid.NewGuid().ToString("N"));
                try
                {
                    var path = System.IO.Path.Combine(folder, "finance.db");
                    var vm = new TransactionsViewModel(new FinanceTracker.Data.SqliteFinanceStore(path));
                    Check(vm.Transactions.Count == 0, "Fresh database must have no sample transactions");
                    vm.AddCommand.Execute(null);
                    vm.Editor.Payee = "Persisted purchase";
                    vm.Editor.AmountText = (-19.95m).ToString(CultureInfo.CurrentCulture);
                    vm.SaveCommand.Execute(null);
                    Check(vm.ErrorMessage == "" && vm.Transactions.Count == 1, "Save should persist new transaction");
                    var reopened = new TransactionsViewModel(new FinanceTracker.Data.SqliteFinanceStore(path));
                    Check(reopened.Transactions.Single().Amount == -19.95m, "Restart should load saved money");
                    reopened.Editor.AmountText = "invalid";
                    reopened.SaveCommand.Execute(null);
                    Check(reopened.Transactions.Single().Amount == -19.95m, "Failed save must preserve original");
                    reopened.CancelCommand.Execute(null);
                    reopened.Editor.AmountText = (24.5m).ToString(CultureInfo.CurrentCulture);
                    reopened.SaveCommand.Execute(null);
                    Check(new FinanceTracker.Data.SqliteFinanceStore(path).LoadTransactions().Single().Amount == 24.5m, "Edit should persist");
                    reopened.AddCommand.Execute(null);
                    reopened.CancelCommand.Execute(null);
                    Check(new FinanceTracker.Data.SqliteFinanceStore(path).LoadTransactions().Count == 1, "Canceled draft must not persist");
                    reopened.DeleteCommand.Execute(null);
                    Check(new FinanceTracker.Data.SqliteFinanceStore(path).LoadTransactions().Count == 0, "Delete should persist");
                }
                finally { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); }
            }),
            ("Main window XAML and bindings instantiate", () =>
            {
                var window = new FinanceTracker.Wpf.MainWindow();
                Check(window.DataContext is TransactionsViewModel, "Window must create its view model");
                window.Measure(new System.Windows.Size(1050, 600));
                window.Arrange(new System.Windows.Rect(0, 0, 1050, 600));
                window.UpdateLayout();
                var vm = (TransactionsViewModel)window.DataContext;
                vm.AddCommand.Execute(null);
                window.UpdateLayout();
                Check(vm.Editor.IsNew && vm.SaveCommand.CanExecute(null), "Bindings must preserve detached draft");
                vm.CancelCommand.Execute(null);
                window.Close();
            }),
            ("Add and Cancel preserve existing data", () =>
            {
                var vm = new TransactionsViewModel();
                var originals = vm.Transactions.ToArray();
                vm.AddCommand.Execute(null);
                Check(vm.Editor.IsNew && vm.SaveCommand.CanExecute(null), "Draft should be editable");
                Check(vm.Transactions.SequenceEqual(originals), "Add must not change saved transactions");
                Check(vm.Accounts.Count == 2 && vm.Categories.Count == 4, "No duplicate lookup entries");
                vm.CancelCommand.Execute(null);
                Check(vm.Transactions.SequenceEqual(originals), "Cancel must discard draft");
            }),
            ("Save commits a new draft exactly once", () =>
            {
                var vm = new TransactionsViewModel();
                vm.AddCommand.Execute(null);
                vm.Editor.Payee = "Test purchase";
                vm.Editor.AmountText = (-12.34m).ToString(CultureInfo.CurrentCulture);
                vm.SaveCommand.Execute(null);
                Check(vm.Transactions.Count == 4, "Save must insert draft");
                Check(vm.SelectedTransaction?.Amount == -12.34m, "Saved transaction should be selected");
                vm.SaveCommand.Execute(null);
                Check(vm.Transactions.Count == 4, "Repeated save must not duplicate");
            }),
            ("Cancel edits leaves saved data unchanged", () =>
            {
                var vm = new TransactionsViewModel();
                var selected = vm.SelectedTransaction!;
                var original = selected.Payee;
                vm.Editor.Payee = "Changed";
                vm.CancelCommand.Execute(null);
                Check(selected.Payee == original && vm.Editor.Payee == original, "Cancel should reload saved values");
            }),
            ("Delete updates total and selects visible rows", () =>
            {
                var vm = new TransactionsViewModel();
                var notified = false;
                vm.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(vm.Total);
                vm.DeleteCommand.Execute(null);
                Check(vm.Total == 2500m && notified, "Delete should notify filtered total");
                vm.DeleteCommand.Execute(null);
                Check(vm.SelectedTransaction is null && !vm.CancelCommand.CanExecute(null), "Hidden rows must not become selected");
            }),
            ("Filtering and saving outside filter stay consistent", () =>
            {
                var vm = new TransactionsViewModel();
                vm.SearchText = "PAYCHECK";
                Check(vm.Total == 2500m && vm.SelectedTransaction?.Payee == "Paycheck", "Case-insensitive search");
                vm.Editor.Payee = "Different payee";
                vm.SaveCommand.Execute(null);
                Check(vm.Total == 0 && vm.SelectedTransaction is null, "Edited row should leave filter");
                vm.SelectedMonth = new DateTime(9999, 12, 1);
                Check(vm.Total == 0, "Maximum date should not overflow");
            }),
            ("Invalid drafts are rejected without insertion", () =>
            {
                var vm = new TransactionsViewModel();
                vm.AddCommand.Execute(null);
                vm.SaveCommand.Execute(null);
                Check(vm.Transactions.Count == 3 && vm.ErrorMessage.Length > 0, "Blank payee should fail");
                vm.Editor.Payee = "Valid";
                vm.Editor.AccountId = 999;
                vm.SaveCommand.Execute(null);
                Check(vm.Transactions.Count == 3 && vm.ErrorMessage.Contains("account"), "Unknown account should fail");
                vm.Editor.AccountId = 1;
                vm.Editor.Date = null;
                vm.SaveCommand.Execute(null);
                Check(vm.Transactions.Count == 3 && vm.ErrorMessage.Contains("Date"), "Missing date should fail");
            }),
            ("Regional amounts are parsed without ambiguous grouping", () =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                    var editor = new TransactionEditorViewModel { Payee = "Test", AmountText = "-12,34" };
                    var transaction = new Transaction();
                    Check(editor.TryApplyTo(transaction, out _) && transaction.Amount == -12.34m, "Comma decimal should work");
                    editor.AmountText = "12.34";
                    Check(!editor.TryApplyTo(transaction, out _), "Wrong separator should be rejected");
                }
                finally { CultureInfo.CurrentCulture = previous; }
            })
        };
        tests = tests.Concat(EdgeCases.All).ToArray();
        var failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS: {test.Name}"); }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL: {test.Name}: {ex.Message}"); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
