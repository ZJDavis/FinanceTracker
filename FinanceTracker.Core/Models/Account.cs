using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinanceTracker.Core.Models;

public sealed class Account
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Type { get; set; } = "Checking";
    public decimal OpeningBalance { get; set; }
}
