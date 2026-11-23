using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Application.Clients;
using FinanceTracker.Application.Contracts;
using FinanceTracker.Core.Models;
using FinanceTracker.Data;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;

namespace FinanceTracker.Wpf.ViewModels;

public partial class TransactionsViewModel
{
    private CancellationTokenSource? refreshCancellation;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string dataModeName = "SQLite (local)";

    [ObservableProperty]
    private string connectionStatus = "";

    [ObservableProperty]
    private int currentPage = 1;

    [ObservableProperty]
    private int totalPages;

    [ObservableProperty]
    private int totalMatches;

    public static async Task<TransactionsViewModel> CreateAsync(IFinanceClient client, CancellationToken cancellationToken = default)
    {
        var viewModel = new TransactionsViewModel(null, startEmpty: true)
        {
            asyncClient = client,
            DataModeName = client.ModeName,
            ConnectionStatus = "Connecting…"
        };

        try
        {
            await client.InitializeAsync(cancellationToken);
            foreach (var account in await client.LoadAccountsAsync(cancellationToken))
                viewModel.Accounts.Add(account);
            foreach (var category in await client.LoadCategoriesAsync(cancellationToken))
                viewModel.Categories.Add(category);

            viewModel.SelectedAccountFilter = viewModel.Accounts.FirstOrDefault();
            await viewModel.RefreshClientDataAsync(resetPage: true, cancellationToken);
        }
        catch (Exception exception)
        {
            viewModel.ConnectionStatus = "Not connected.";
            viewModel.ErrorMessage = "Could not load finance data: " + exception.Message;
        }

        return viewModel;
    }

    private async Task RefreshClientDataAsync(bool resetPage, CancellationToken cancellationToken = default)
    {
        if (asyncClient is null) return;
        if (resetPage) CurrentPage = 1;

        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var refreshToken = refreshCancellation.Token;
        IsBusy = true;
        NotifyAsyncCommands();

        try
        {
            var monthStart = new DateOnly(SelectedMonth.Year, SelectedMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var query = new TransactionQuery(
                SelectedAccountFilter?.Id,
                null,
                monthStart,
                monthEnd,
                SearchText,
                "date",
                "desc",
                CurrentPage,
                25);

            var page = await asyncClient.GetTransactionsAsync(query, refreshToken);
            var activity = await asyncClient.GetActivityAsync(monthStart, monthEnd, SelectedAccountFilter?.Id, refreshToken);
            refreshToken.ThrowIfCancellationRequested();

            var selectedId = SelectedTransaction?.Id;
            Transactions.Clear();
            foreach (var transaction in page.Items)
                Transactions.Add(transaction);

            remoteActivity = activity;
            CurrentPage = page.Page;
            TotalPages = page.TotalPages;
            TotalMatches = page.TotalCount;
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(TotalWarning));
            TransactionsView.Refresh();

            SelectedTransaction = selectedId is int transactionId
                ? Transactions.FirstOrDefault(transaction => transaction.Id == transactionId) ?? Transactions.FirstOrDefault()
                : Transactions.FirstOrDefault();

            ConnectionStatus = asyncClient.ModeName == "API (Docker)"
                ? "Connected to the local demo API."
                : "Local database ready.";
            if (page.TotalCount == 0) ConnectionStatus = "Connected. No transactions match these filters.";
            ErrorMessage = "";
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
            // A newer search or filter request replaced this one.
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Connection problem. Your current rows and draft are still available.";
            ErrorMessage = "Could not refresh finance data: " + exception.Message;
        }
        finally
        {
            if (refreshCancellation?.Token == refreshToken)
            {
                IsBusy = false;
                NotifyAsyncCommands();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseClient))]
    private Task AddClientAsync()
    {
        if (asyncClient is null) return Task.CompletedTask;
        SelectedTransaction = null;
        var draft = new Transaction
        {
            Id = 0,
            AccountId = SelectedAccountFilter?.Id ?? Accounts.FirstOrDefault()?.Id ?? 0,
            CategoryId = Categories.FirstOrDefault(category => category.Name == "Demo Groceries")?.Id ?? Categories.FirstOrDefault()?.Id ?? 0,
            Date = DateOnly.FromDateTime(SelectedMonth)
        };
        _newItemDraft = draft;
        _editingTarget = draft;
        Editor.LoadFrom(draft, isNew: true);
        ErrorMessage = "";
        NotifyAsyncCommands();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteClient))]
    private async Task DeleteClientAsync()
    {
        if (asyncClient is null || SelectedTransaction is null) return;
        var selected = SelectedTransaction;
        IsBusy = true;
        NotifyAsyncCommands();
        try
        {
            await asyncClient.DeleteTransactionAsync(selected.Id, selected.Version);
            Transactions.Remove(selected);
            SelectedTransaction = null;
            await RefreshClientDataAsync(resetPage: false);
        }
        catch (Exception exception)
        {
            ErrorMessage = "Delete failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyAsyncCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveClient))]
    private async Task SaveClientAsync()
    {
        if (asyncClient is null || _editingTarget is null) return;
        if (!Accounts.Any(account => account.Id == Editor.AccountId))
        {
            ErrorMessage = "Select a valid account.";
            return;
        }
        if (!Categories.Any(category => category.Id == Editor.CategoryId))
        {
            ErrorMessage = "Select a valid category.";
            return;
        }

        var savedDraft = new Transaction { Id = _editingTarget.Id, Version = _editingTarget.Version };
        if (!Editor.TryApplyTo(savedDraft, out var error))
        {
            ErrorMessage = error ?? "Cannot save transaction.";
            return;
        }

        IsBusy = true;
        NotifyAsyncCommands();
        try
        {
            var saved = await asyncClient.SaveTransactionAsync(savedDraft);
            var currentRow = Transactions.FirstOrDefault(transaction => transaction.Id == saved.Id);
            if (currentRow is null)
                Transactions.Add(saved);
            else
                Transactions[Transactions.IndexOf(currentRow)] = saved;

            _editingTarget = saved;
            _newItemDraft = null;
            ErrorMessage = "";
            Editor.LoadFrom(saved, isNew: false);
            await RefreshClientDataAsync(resetPage: true);
        }
        catch (FinanceApiException exception) when ((int)exception.StatusCode == 412)
        {
            ErrorMessage = "This transaction changed on the server. Your edits are preserved; use Reload to load the latest version or Cancel to discard them.";
        }
        catch (Exception exception)
        {
            ErrorMessage = "Save failed. Your draft is preserved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyAsyncCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseClient))]
    private Task CancelClientAsync()
    {
        if (_editingTarget is null) return Task.CompletedTask;
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
        NotifyAsyncCommands();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanReloadClient))]
    private async Task ReloadClientAsync()
    {
        if (asyncClient is null || SelectedTransaction is null) return;
        IsBusy = true;
        NotifyAsyncCommands();
        try
        {
            var latest = await asyncClient.GetTransactionAsync(SelectedTransaction.Id);
            var currentRow = Transactions.FirstOrDefault(transaction => transaction.Id == latest.Id);
            if (currentRow is not null)
                Transactions[Transactions.IndexOf(currentRow)] = latest;
            _newItemDraft = null;
            SelectedTransaction = latest;
            _editingTarget = latest;
            Editor.LoadFrom(latest, isNew: false);
            ErrorMessage = "Latest server version loaded. Previous draft edits were discarded.";
        }
        catch (Exception exception)
        {
            ErrorMessage = "Reload failed. Your edits are preserved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyAsyncCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageClientAsync()
    {
        CurrentPage--;
        await RefreshClientDataAsync(resetPage: false);
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageClientAsync()
    {
        CurrentPage++;
        await RefreshClientDataAsync(resetPage: false);
    }

    [RelayCommand(CanExecute = nameof(CanUseClient))]
    private async Task RetryClientAsync()
    {
        if (asyncClient is null) return;
        IsBusy = true;
        NotifyAsyncCommands();
        try
        {
            await asyncClient.InitializeAsync();
            if (Accounts.Count == 0)
            {
                foreach (var account in await asyncClient.LoadAccountsAsync()) Accounts.Add(account);
                foreach (var category in await asyncClient.LoadCategoriesAsync()) Categories.Add(category);
                SelectedAccountFilter = Accounts.FirstOrDefault();
            }
            await RefreshClientDataAsync(resetPage: false);
            ErrorMessage = "";
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Still offline. Check the API and try again.";
            ErrorMessage = "Reconnect failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyAsyncCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseClient))]
    private async Task ImportTransactionsAsync()
    {
        if (asyncClient is null) return;

        var filePicker = new OpenFileDialog
        {
            Title = "Choose a transaction CSV file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (filePicker.ShowDialog(Application.Current?.MainWindow) != true) return;

        IsBusy = true;
        NotifyAsyncCommands();
        try
        {
            // Scan every page so duplicate warnings cover the full active database.
            var firstPage = await asyncClient.GetTransactionsAsync(new TransactionQuery(
                null, null, null, null, null, "date", "asc", 1, 100));
            var existingTransactions = firstPage.Items.ToList();
            for (var pageNumber = 2; pageNumber <= firstPage.TotalPages; pageNumber++)
            {
                var page = await asyncClient.GetTransactionsAsync(new TransactionQuery(
                    null, null, null, null, null, "date", "asc", pageNumber, 100));
                existingTransactions.AddRange(page.Items);
            }

            var previewRows = CsvTransactionImportParser.ReadPreview(
                filePicker.FileName, Accounts.ToArray(), Categories.ToArray(), existingTransactions);
            var previewWindow = new CsvImportPreviewWindow(previewRows)
            {
                Owner = Application.Current?.MainWindow
            };
            if (previewWindow.ShowDialog() != true) return;

            var selectedRows = previewRows.Where(row => row.Include && row.IsValid && row.Transaction is not null).ToArray();
            var importedCount = 0;
            var failures = new List<string>();
            foreach (var row in selectedRows)
            {
                try
                {
                    await asyncClient.SaveTransactionAsync(row.Transaction!);
                    importedCount++;
                }
                catch (Exception exception)
                {
                    failures.Add($"CSV row {row.RowNumber}: {exception.Message}");
                }
            }

            if (selectedRows.Length > 0 && selectedRows[0].Transaction is { } firstImportedTransaction)
            {
                // Put the imported account in view so the user can immediately see the new rows.
                SelectedAccountFilter = Accounts.FirstOrDefault(account => account.Id == firstImportedTransaction.AccountId);
            }
            await RefreshClientDataAsync(resetPage: true);
            var skippedCount = previewRows.Count - selectedRows.Length;
            ErrorMessage = failures.Count == 0
                ? $"Imported {importedCount} transaction(s); skipped {skippedCount} row(s)."
                : $"Imported {importedCount}; {failures.Count} row(s) failed; skipped {skippedCount}. " + string.Join(" | ", failures.Take(3));
        }
        catch (Exception exception)
        {
            ErrorMessage = "CSV import could not be prepared: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyAsyncCommands();
        }
    }

    private bool CanUseClient() => asyncClient is not null && !IsBusy;
    private bool CanSaveClient() => CanUseClient() && _editingTarget is not null;
    private bool CanDeleteClient() => CanUseClient() && SelectedTransaction is not null;
    private bool CanReloadClient() => CanUseClient() && SelectedTransaction is not null;
    private bool CanPreviousPage() => CanUseClient() && CurrentPage > 1;
    private bool CanNextPage() => CanUseClient() && CurrentPage < TotalPages;

    private void NotifyAsyncCommands()
    {
        AddClientCommand.NotifyCanExecuteChanged();
        DeleteClientCommand.NotifyCanExecuteChanged();
        SaveClientCommand.NotifyCanExecuteChanged();
        CancelClientCommand.NotifyCanExecuteChanged();
        ReloadClientCommand.NotifyCanExecuteChanged();
        PreviousPageClientCommand.NotifyCanExecuteChanged();
        NextPageClientCommand.NotifyCanExecuteChanged();
        RetryClientCommand.NotifyCanExecuteChanged();
        ImportTransactionsCommand.NotifyCanExecuteChanged();
    }
}
