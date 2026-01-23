using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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

    public decimal Total => TransactionsView.Cast<Transaction>().Sum(t => t.Amount);

    public TransactionEditorViewModel Editor { get; } = new();
    private Transaction? _editingTarget;
    private Transaction? _newItemDraft;

    public TransactionsViewModel()
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

        TransactionsView = CollectionViewSource.GetDefaultView(Transactions);
        TransactionsView.Filter = FilterTransaction;

        SelectedAccountFilter = Accounts.FirstOrDefault();

        SelectedTransaction = Transactions.FirstOrDefault();
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
        var monthEnd = monthStart.AddMonths(1);

        if (t.Date < monthStart || t.Date >= monthEnd)
            return false;

        // Search filter
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var s = SearchText.Trim();

        return (t.Payee?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Memo?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnSelectedTransactionChanged(Transaction? value)
    {
        if (value is null)
        {
            _editingTarget = null;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            return;
        }

        _editingTarget = value;
        _newItemDraft = null; // selecting an existing item cancels "new draft" mode
        Editor.LoadFrom(value, isNew: false);

        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Add()
    {
        var nextId = Transactions.Count == 0 ? 1 : Transactions.Max(x => x.Id) + 1;

        var t = new Transaction
        {
            Id = nextId,
            AccountId = SelectedAccountFilter?.Id ?? Accounts.First().Id,
            CategoryId = Categories.First(c => c.Name == "Uncategorized").Id,
            Date = DateOnly.FromDateTime(DateTime.Today),
            Payee = "",
            Memo = "",
            Amount = 0m
        };

        Transactions.Add(t);
        _newItemDraft = t;

        SelectedTransaction = t;           // loads editor
        Editor.LoadFrom(t, isNew: true);   // explicitly new

        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();

        Accounts.Add(new Account { Id = 1, Name = "Checking", Type = "Checking", OpeningBalance = 1000m });
        Accounts.Add(new Account { Id = 2, Name = "Credit Card", Type = "Credit", OpeningBalance = 0m });

        Categories.Add(new Category { Id = 1, Name = "Groceries", Kind = "Expense" });
        Categories.Add(new Category { Id = 2, Name = "Rent", Kind = "Expense" });
        Categories.Add(new Category { Id = 3, Name = "Salary", Kind = "Income" });
        Categories.Add(new Category { Id = 4, Name = "Uncategorized", Kind = "Expense" });
        Transactions.Clear();
        Transactions.Add(new Transaction { Id = 1, AccountId = 1, CategoryId = 1, Payee = "Grocery Store", Memo = "Weekly groceries", Amount = -54.23m });
        Transactions.Add(new Transaction { Id = 2, AccountId = 1, CategoryId = 3, Payee = "Paycheck", Memo = "Salary", Amount = 2500m });
        Transactions.Add(new Transaction { Id = 3, AccountId = 2, CategoryId = 2, Payee = "Landlord", Memo = "Jan rent", Amount = -1200m });
        SelectedAccountFilter = Accounts.FirstOrDefault();


    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedTransaction is null) return;

        var toRemove = SelectedTransaction;
        Transactions.Remove(toRemove);

        SelectedTransaction = Transactions.FirstOrDefault();
    }

    private bool CanDelete() => SelectedTransaction is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_editingTarget is null) return;

        if (!Editor.TryApplyTo(_editingTarget, out var error))
        {
            MessageBox.Show(error, "Cannot Save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // If it was a new item and it saved successfully, it’s no longer a draft
        _newItemDraft = null;
        Editor.LoadFrom(_editingTarget, isNew: false);

        RefreshView();

        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => _editingTarget is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_editingTarget is null) return;

        // If canceling a new draft, remove it
        if (_newItemDraft == _editingTarget)
        {
            Transactions.Remove(_editingTarget);
            _newItemDraft = null;
            SelectedTransaction = Transactions.FirstOrDefault();
            return;
        }

        // Otherwise revert editor to the selected item’s current values
        Editor.LoadFrom(_editingTarget, isNew: false);
    }

    private bool CanCancel() => _editingTarget is not null;

    private void RefreshView()
    {
        TransactionsView.Refresh();
        OnPropertyChanged(nameof(Total));
    }
}
