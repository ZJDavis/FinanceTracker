using FinanceTracker.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Linq;

namespace FinanceTracker.Wpf.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    public ObservableCollection<Transaction> Transactions { get; } = new();
    public ICollectionView TransactionsView { get; }
    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();

    [ObservableProperty]
    private Account? selectedAccountFilter;

    [ObservableProperty]
    private DateTime selectedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private Transaction? selectedTransaction;

    [ObservableProperty]
    private string searchText = "";

    public decimal? Total
    {
        get
        {
            try { return TransactionsView.Cast<Transaction>().Sum(t => t.Amount); }
            catch (OverflowException) { return null; }
        }
    }
    public string TotalWarning => Total is null ? "Filtered total exceeds the supported numeric range." : "";

    public TransactionEditorViewModel Editor { get; } = new();
    private Transaction? _editingTarget;
    private Transaction? _newItemDraft;

    private readonly IFinanceStore? store;
    public TransactionsViewModel() : this(null) { }
    public TransactionsViewModel(IFinanceStore? store)
    {
        this.store = store;
        if (store is null)
        {
            // Seed accounts
            Accounts.Add(new Account { Id = 1, Name = "Checking", Type = "Checking", OpeningBalance = 1000m });
            Accounts.Add(new Account { Id = 2, Name = "Credit Card", Type = "Credit", OpeningBalance = 0m });

            // Seed categories
            Categories.Add(new Category { Id = 1, Name = "Groceries", Kind = "Expense" });
            Categories.Add(new Category { Id = 2, Name = "Rent", Kind = "Expense" });
            Categories.Add(new Category { Id = 3, Name = "Salary", Kind = "Income" });
            Categories.Add(new Category { Id = 4, Name = "Uncategorized", Kind = "Expense" });

            // Seed transactions
            Transactions.Add(new Transaction { Id = 1, AccountId = 1, CategoryId = 1, Payee = "Grocery Store", Memo = "Weekly groceries", Amount = -54.23m });
            Transactions.Add(new Transaction { Id = 2, AccountId = 1, CategoryId = 3, Payee = "Paycheck", Memo = "Salary", Amount = 2500m });
            Transactions.Add(new Transaction { Id = 3, AccountId = 2, CategoryId = 2, Payee = "Landlord", Memo = "Jan rent", Amount = -1200m });

        }
        else
        {
            store.Initialize();
            foreach (var account in store.LoadAccounts()) Accounts.Add(account);
            foreach (var category in store.LoadCategories()) Categories.Add(category);
            foreach (var item in store.LoadTransactions()) Transactions.Add(item);
        }
        TransactionsView = CollectionViewSource.GetDefaultView(Transactions);
        TransactionsView.Filter = FilterTransaction;

        SelectedAccountFilter = Accounts.FirstOrDefault();

        SelectedTransaction = TransactionsView.Cast<Transaction>().FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnSelectedAccountFilterChanged(Account? value) => RefreshView();

    partial void OnSelectedMonthChanged(DateTime value) => RefreshView();

    private bool FilterTransaction(object obj)
    {
        if (obj is not Transaction t) return false;

        // Account filter
        if (SelectedAccountFilter is not null && t.AccountId != SelectedAccountFilter.Id)
            return false;

        // Month filter (Transaction.Date is DateOnly)
        var monthStart = new DateOnly(SelectedMonth.Year, SelectedMonth.Month, 1);


        if (t.Date.Year != monthStart.Year || t.Date.Month != monthStart.Month)
            return false;

        // Search filter
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var s = SearchText.Trim();

        return (t.Payee?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Memo?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnSelectedTransactionChanged(Transaction? value)
    {
        // Selecting another row discards the editor's unsaved changes.
        _newItemDraft = null;
        _editingTarget = value;
        Editor.LoadFrom(value ?? new Transaction(), isNew: false);
        ErrorMessage = "";
        NotifyCommands();
    }

    [ObservableProperty]
    private string errorMessage = "";

    [RelayCommand]
    private void Add()
    {
        SelectedTransaction = null;
        var draft = new Transaction
        {
            Id = 0,
            AccountId = SelectedAccountFilter?.Id ?? Accounts.FirstOrDefault()?.Id ?? 0,
            CategoryId = Categories.FirstOrDefault(c => c.Name == "Uncategorized")?.Id ?? 0,
            Date = DateOnly.FromDateTime(SelectedMonth)
        };
        _newItemDraft = draft;
        _editingTarget = draft;
        Editor.LoadFrom(draft, isNew: true);
        ErrorMessage = "";
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedTransaction is null) return;
        try { store?.DeleteTransaction(SelectedTransaction.Id, SelectedTransaction.Version); }
        catch (Exception ex) { ErrorMessage = "Delete failed: " + ex.Message; return; }
        Transactions.Remove(SelectedTransaction);
        SelectedTransaction = null;
        RefreshView();
    }

    private bool CanDelete() => SelectedTransaction is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_editingTarget is null) return;
        if (!Accounts.Any(a => a.Id == Editor.AccountId))
        {
            ErrorMessage = "Select a valid account.";
            return;
        }
        if (!Categories.Any(c => c.Id == Editor.CategoryId))
        {
            ErrorMessage = "Select a valid category.";
            return;
        }
        var saved = new Transaction { Id = _editingTarget.Id, Version = _editingTarget.Version };
        if (!Editor.TryApplyTo(saved, out var error))
        {
            ErrorMessage = error ?? "Cannot save transaction.";
            return;
        }

        try
        {
            saved.Id = store?.SaveTransaction(saved) ?? (saved.Id == 0 ? (Transactions.Count == 0 ? 1 : Transactions.Max(t => t.Id) + 1) : saved.Id);
        }
        catch (Exception ex) { ErrorMessage = "Save failed: " + ex.Message; return; }
        if (_newItemDraft is not null) Transactions.Add(saved);
        else Transactions[Transactions.IndexOf(_editingTarget)] = saved;
        _editingTarget = saved;
        _newItemDraft = null;
        ErrorMessage = "";
        Editor.LoadFrom(saved, isNew: false);
        RefreshView();
        // A saved transaction can disappear from the current filters.
        SelectedTransaction = TransactionsView.Cast<Transaction>().Contains(saved)
            ? saved : TransactionsView.Cast<Transaction>().FirstOrDefault();
        if (SelectedTransaction is null)
        {
            _editingTarget = null;
            Editor.LoadFrom(new Transaction(), isNew: false);
        }
        NotifyCommands();
    }

    private bool CanSave() => _editingTarget is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_editingTarget is null) return;
        ErrorMessage = "";
        if (_newItemDraft is not null)
        {
            _newItemDraft = null;
            _editingTarget = null;
            Editor.LoadFrom(new Transaction(), isNew: false);
            SelectedTransaction = TransactionsView.Cast<Transaction>().FirstOrDefault();
        }
        else
        {
            Editor.LoadFrom(_editingTarget, isNew: false);
        }
        NotifyCommands();
    }

    private bool CanCancel() => _editingTarget is not null;

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void RefreshView()
    {
        TransactionsView.Refresh();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalWarning));
        if (_newItemDraft is null &&
            (SelectedTransaction is null || !TransactionsView.Cast<Transaction>().Contains(SelectedTransaction)))
        {
            SelectedTransaction = TransactionsView.Cast<Transaction>().FirstOrDefault();
        }
    }
}
