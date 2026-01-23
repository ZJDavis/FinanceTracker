using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinanceTracker.Core.Models;

public sealed class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public int AccountId { get; set; }
    public int CategoryId { get; set; }

    public string Payee { get; set; } = "";
    public string Memo { get; set; } = "";

    // Convention: expenses negative, income positive
    public decimal Amount { get; set; }
}

