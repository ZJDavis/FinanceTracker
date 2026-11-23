using System.Net.Http.Json;
using System.Text.Json;
using FinanceTracker.Application.Clients;
using FinanceTracker.Application.Contracts;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Data;

/// <summary>
/// HTTP client for the versioned FinanceTracker API. Version ETags are kept on
/// each transaction and sent back for edits and deletes.
/// </summary>
public sealed class HttpFinanceClient(HttpClient httpClient) : IFinanceClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ModeName => "API (Docker)";

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<Account>> LoadAccountsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/accounts", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var accounts = await response.Content.ReadFromJsonAsync<AccountResponse[]>(JsonOptions, cancellationToken) ?? [];
        return accounts.Select(account => new Account
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            OpeningBalance = account.OpeningBalance
        }).ToArray();
    }

    public async Task<IReadOnlyList<Category>> LoadCategoriesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/categories", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var categories = await response.Content.ReadFromJsonAsync<CategoryResponse[]>(JsonOptions, cancellationToken) ?? [];
        return categories.Select(category => new Category
        {
            Id = category.Id,
            Name = category.Name,
            Kind = category.Kind
        }).ToArray();
    }

    public async Task<PageResponse<Transaction>> GetTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>
        {
            $"sort={Uri.EscapeDataString(query.Sort)}",
            $"direction={Uri.EscapeDataString(query.Direction)}",
            $"page={query.Page}",
            $"pageSize={query.PageSize}"
        };
        if (query.AccountId is int accountId) queryParts.Add($"accountId={accountId}");
        if (query.CategoryId is int categoryId) queryParts.Add($"categoryId={categoryId}");
        if (query.From is DateOnly from) queryParts.Add($"from={from:yyyy-MM-dd}");
        if (query.To is DateOnly to) queryParts.Add($"to={to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(query.Search)) queryParts.Add($"search={Uri.EscapeDataString(query.Search.Trim())}");

        using var response = await httpClient.GetAsync("api/v1/transactions?" + string.Join('&', queryParts), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResponse<TransactionResponse>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty transactions response.");
        return new PageResponse<Transaction>(page.Items.Select(ToModel).ToArray(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);
    }

    public async Task<ActivityResponse> GetActivityAsync(DateOnly from, DateOnly to, int? accountId, CancellationToken cancellationToken = default)
    {
        var requestUri = $"api/v1/reports/activity?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (accountId is int selectedAccountId) requestUri += $"&accountId={selectedAccountId}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ActivityResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty activity summary.");
    }

    public async Task<Transaction> GetTransactionAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/transactions/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var transaction = await response.Content.ReadFromJsonAsync<TransactionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty transaction.");
        return ToModel(transaction);
    }

    public async Task<Transaction> SaveTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        var request = new TransactionRequest(transaction.Date, transaction.AccountId, transaction.CategoryId,
            transaction.Payee, transaction.Memo, transaction.Amount, transaction.SyncId);

        using var httpRequest = transaction.Id == 0
            ? new HttpRequestMessage(HttpMethod.Post, "api/v1/transactions") { Content = JsonContent.Create(request, options: JsonOptions) }
            : new HttpRequestMessage(HttpMethod.Put, $"api/v1/transactions/{transaction.Id}") { Content = JsonContent.Create(request, options: JsonOptions) };

        if (transaction.Id != 0)
            httpRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{transaction.Version}\"");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var saved = await response.Content.ReadFromJsonAsync<TransactionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty saved transaction.");
        return ToModel(saved);
    }

    public async Task DeleteTransactionAsync(int id, int? version = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v1/transactions/{id}");
        if (version is int expectedVersion)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static Transaction ToModel(TransactionResponse transaction)
        => new()
        {
            Id = transaction.Id,
            SyncId = transaction.SyncId,
            Version = transaction.Version,
            Date = transaction.Date,
            AccountId = transaction.AccountId,
            CategoryId = transaction.CategoryId,
            Payee = transaction.Payee,
            Memo = transaction.Memo,
            Amount = transaction.Amount
        };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = "api_request_failed";
        var message = $"The API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        try
        {
            using var problem = JsonDocument.Parse(responseText);
            if (problem.RootElement.TryGetProperty("code", out var problemCode))
                code = problemCode.GetString() ?? code;
            if (problem.RootElement.TryGetProperty("detail", out var detail))
                message = detail.GetString() ?? message;
            else if (problem.RootElement.TryGetProperty("title", out var title))
                message = title.GetString() ?? message;
        }
        catch (JsonException)
        {
            // Some proxies return plain text or an empty body; keep a safe status-based message.
        }

        throw new FinanceApiException(response.StatusCode, code, message);
    }

    public void Dispose() => httpClient.Dispose();
}
