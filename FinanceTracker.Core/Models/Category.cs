using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinanceTracker.Core.Models;

public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Kind { get; set; } = "Expense"; // Income / Expense / Transfer
}

