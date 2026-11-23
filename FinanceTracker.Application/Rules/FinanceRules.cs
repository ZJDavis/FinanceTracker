using FinanceTracker.Application.Contracts;

namespace FinanceTracker.Application.Rules;

/// <summary>
/// Shared finance rules live outside the HTTP and database layers so both can
/// use the same decisions and tests can exercise them without a database.
/// </summary>
public static class FinanceRules
{
    public static void ValidateTransaction(
        TransactionRequest request,
        bool accountExists,
        bool accountIsActive,
        bool categoryExists,
        bool categoryIsActive,
        string categoryKind)
    {
        if (string.IsNullOrWhiteSpace(request.Payee))
            throw new FinanceValidationException("payee_required", "Payee is required.");
        if (request.Payee.Trim().Length > 200)
            throw new FinanceValidationException("payee_too_long", "Payee must be 200 characters or fewer.");
        if ((request.Memo?.Length ?? 0) > 2000)
            throw new FinanceValidationException("memo_too_long", "Memo must be 2,000 characters or fewer.");
        if (request.Amount == 0)
            throw new FinanceValidationException("amount_must_be_nonzero", "Amount must not be zero.");
        if (!accountExists)
            throw new FinanceValidationException("account_not_found", "The selected account does not exist.");
        if (!accountIsActive)
            throw new FinanceValidationException("account_inactive", "The selected account is inactive.");
        if (!categoryExists)
            throw new FinanceValidationException("category_not_found", "The selected category does not exist.");
        if (!categoryIsActive)
            throw new FinanceValidationException("category_inactive", "The selected category is inactive.");
        if (categoryKind == "Transfer")
            throw new FinanceValidationException("transfer_not_supported", "Transfer categories are unavailable until paired-account transfers are designed.");
        if ((categoryKind == "Income" && request.Amount < 0) || (categoryKind == "Expense" && request.Amount > 0))
            throw new FinanceValidationException("category_amount_sign_mismatch", "Income must be positive and expense must be negative.");
        if (categoryKind is not ("Income" or "Expense"))
            throw new FinanceValidationException("category_kind_invalid", "The selected category has an unsupported kind.");
    }

    public static void ValidateDateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new FinanceValidationException("invalid_date_range", "The start date must be on or before the end date.");
    }

    public static void ValidateTransactionQuery(TransactionQuery query)
    {
        if (query.From is DateOnly from && query.To is DateOnly to)
            ValidateDateRange(from, to);
        if (query.Search?.Trim().Length > 100)
            throw new FinanceValidationException("search_too_long", "Search text must be 100 characters or fewer.");
        if (query.Page < 1)
            throw new FinanceValidationException("page_invalid", "Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw new FinanceValidationException("page_size_invalid", "Page size must be between 1 and 100.");
        if (query.Sort is not ("date" or "amount" or "payee"))
            throw new FinanceValidationException("sort_invalid", "Sort must be date, amount, or payee.");
        if (query.Direction is not ("asc" or "desc"))
            throw new FinanceValidationException("direction_invalid", "Direction must be asc or desc.");
    }
}
