using System.Globalization;
using FinanceTracker.Core.Models;
using FinanceTracker.Wpf.ViewModels;
using Xunit;

public sealed partial class TransactionEditorTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("0.001", "0.001")]
    [InlineData("  +12.34  ", "12.34")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    public void SupportedAmountsParseExactly(string input, string expected)
    {
        WithCulture("en-US", () =>
        {
            var editor = new TransactionEditorViewModel { Payee = "Test", AmountText = input };
            var item = new Transaction(); Assert.True(editor.TryApplyTo(item, out _));
            Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), item.Amount);
        });
    }
    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e2")]
    [InlineData("1,234.56")]
    [InlineData("$12.34")]
    [InlineData("--1")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0); DROP TABLE Transactions; --")]
    public void InvalidAmountsAreRejectedWithoutMutation(string input)
    {
        WithCulture("en-US", () =>
        {
            var editor = new TransactionEditorViewModel { Payee = "Changed", AmountText = input };
            var item = new Transaction { Payee = "Original", Amount = 7m };
            Assert.False(editor.TryApplyTo(item, out var error)); Assert.NotNull(error);
            Assert.Equal("Original", item.Payee); Assert.Equal(7m, item.Amount);
        });
    }
    [Theory]
    [InlineData("en-US", "12.34")]
    [InlineData("de-DE", "12,34")]
    [InlineData("ru-RU", "12,34")]
    public void RegionalSeparatorsWork(string culture, string input)
    {
        WithCulture(culture, () =>
        {
            var editor = new TransactionEditorViewModel { Payee = "Test", AmountText = input };
            var item = new Transaction(); Assert.True(editor.TryApplyTo(item, out _)); Assert.Equal(12.34m, item.Amount);
        });
    }
    [Fact]
    public void LoadingThenSavingDoesNotRoundStoredPrecision()
    {
        var item = new Transaction { Payee = "Exact", Amount = 0.1234567890123456789012345678m };
        var editor = new TransactionEditorViewModel(); editor.LoadFrom(item, false);
        var result = new Transaction(); Assert.True(editor.TryApplyTo(result, out _)); Assert.Equal(item.Amount, result.Amount);
    }
    private static void WithCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
