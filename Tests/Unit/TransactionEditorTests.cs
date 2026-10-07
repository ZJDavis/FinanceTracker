using FinanceTracker.Core.Models;
using FinanceTracker.Wpf.ViewModels;
using Xunit;

public sealed partial class TransactionEditorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPayeeDoesNotMutateTransaction(string payee)
    {
        var original = new Transaction { Payee = "Original", Amount = 42m };
        var editor = new TransactionEditorViewModel { Payee = payee, AmountText = "100" };
        Assert.False(editor.TryApplyTo(original, out var error));
        Assert.NotNull(error); Assert.Equal("Original", original.Payee); Assert.Equal(42m, original.Amount);
    }
    [Fact]
    public void MissingDateIsRejected()
    {
        var editor = new TransactionEditorViewModel { Payee = "Valid", Date = null };
        Assert.False(editor.TryApplyTo(new Transaction(), out _));
    }
    [Fact]
    public void SavingTrimsTextAndCopiesAssociations()
    {
        var editor = new TransactionEditorViewModel { Payee = " Purchase ", Memo = " Note ", AccountId = 2, CategoryId = 3, AmountText = "-25", Date = new DateTime(2024, 2, 29) };
        var item = new Transaction();
        Assert.True(editor.TryApplyTo(item, out _));
        Assert.Equal("Purchase", item.Payee); Assert.Equal("Note", item.Memo);
        Assert.Equal(2, item.AccountId); Assert.Equal(3, item.CategoryId);
        Assert.Equal(-25m, item.Amount); Assert.Equal(new DateOnly(2024, 2, 29), item.Date);
    }
}
