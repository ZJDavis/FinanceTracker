using FinanceTracker.Application.Contracts;
using FinanceTracker.Application.Rules;
using Xunit;

namespace FinanceTracker.UnitTests;

public sealed class FinanceRulesTests
{
    [Fact]
    public void ValidExpenseUsesNegativeAmount()
    {
        var request = new TransactionRequest(new DateOnly(2026, 4, 1), 1, 1, "Market", null, -12.35m);

        FinanceRules.ValidateTransaction(request, true, true, true, true, "Expense");
    }

    [Theory]
    [InlineData(12.35, "Expense")]
    [InlineData(-12.35, "Income")]
    public void CategoryKindMustMatchAmountSign(decimal amount, string categoryKind)
    {
        var request = new TransactionRequest(new DateOnly(2026, 4, 1), 1, 1, "Market", null, amount);

        var exception = Assert.Throws<FinanceValidationException>(() =>
            FinanceRules.ValidateTransaction(request, true, true, true, true, categoryKind));

        Assert.Equal("category_amount_sign_mismatch", exception.Code);
    }

    [Fact]
    public void ZeroAmountIsRejected()
    {
        var request = new TransactionRequest(new DateOnly(2026, 4, 1), 1, 1, "Market", null, 0m);

        var exception = Assert.Throws<FinanceValidationException>(() =>
            FinanceRules.ValidateTransaction(request, true, true, true, true, "Expense"));

        Assert.Equal("amount_must_be_nonzero", exception.Code);
    }

    [Fact]
    public void InactiveReferencesAreRejected()
    {
        var request = new TransactionRequest(new DateOnly(2026, 4, 1), 1, 1, "Market", null, -12.35m);

        var exception = Assert.Throws<FinanceValidationException>(() =>
            FinanceRules.ValidateTransaction(request, true, false, true, true, "Expense"));

        Assert.Equal("account_inactive", exception.Code);
    }

    [Fact]
    public void InvalidPageSizeIsRejected()
    {
        var query = new TransactionQuery(null, null, null, null, null, "date", "desc", 1, 101);

        var exception = Assert.Throws<FinanceValidationException>(() => FinanceRules.ValidateTransactionQuery(query));

        Assert.Equal("page_size_invalid", exception.Code);
    }
}
