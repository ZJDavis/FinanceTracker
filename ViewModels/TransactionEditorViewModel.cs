using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FinanceTracker.Core.Models;
using System.Globalization;

namespace FinanceTracker.Wpf.ViewModels;

public partial class TransactionEditorViewModel : ObservableObject
{
    [ObservableProperty] private int id;
    [ObservableProperty] private int accountId;
    [ObservableProperty] private int categoryId;
    [ObservableProperty] private DateTime? date = DateTime.Today;
    [ObservableProperty] private string payee = "";
    [ObservableProperty] private string memo = "";
    [ObservableProperty] private string amountText = "0.00";

    // Tracks whether the editor is for a brand-new item
    public bool IsNew { get; private set; }

    public void LoadFrom(Transaction t, bool isNew)
    {
        IsNew = isNew;
        Id = t.Id;
        Date = t.Date.ToDateTime(TimeOnly.MinValue);
        Payee = t.Payee ?? "";
        Memo = t.Memo ?? "";
        AmountText = t.Amount.ToString("0.00##########################", CultureInfo.CurrentCulture);
        AccountId = t.AccountId;
        CategoryId = t.CategoryId;
    }

    public bool TryApplyTo(Transaction t, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(Payee))
        {
            error = "Payee is required.";
            return false;
        }

        if (Date is null)
        {
            error = "Date is required.";
            return false;
        }

        if (!decimal.TryParse(AmountText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var amt))
        {
            error = "Amount must be a valid number using your regional decimal separator.";
            return false;
        }

        t.Date = DateOnly.FromDateTime(Date.Value);
        t.Payee = Payee.Trim();
        t.Memo = Memo?.Trim() ?? "";
        t.Amount = amt;
        t.AccountId = AccountId;
        t.CategoryId = CategoryId;

        return true;
    }
}
