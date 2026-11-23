using System.Net;
using System.Net.Http.Json;
using FinanceTracker.Application.Contracts;
using Xunit;

namespace FinanceTracker.Api.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class TransactionApiTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CleanDatabaseAppliesMigrationAndSeedsDemoReferenceDataOnlyOnce()
    {
        using var client = fixture.ApiFactory.CreateClient();

        var accounts = await client.GetFromJsonAsync<AccountResponse[]>("/api/v1/accounts");
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/v1/categories");
        Assert.NotNull(accounts);
        Assert.NotNull(categories);
        Assert.Equal(3, accounts.Length);
        Assert.Equal(23, categories.Length);

        var dateRange = "?from=2026-01-01&to=2026-03-31";
        var seededTransactions = await client.GetFromJsonAsync<PageResponse<TransactionResponse>>("/api/v1/transactions" + dateRange);
        Assert.Equal(10, seededTransactions!.TotalCount);
    }

    [Fact]
    public async Task CreateReturnsExactDecimalAndStrongEtagThenStaleEditGets412()
    {
        await fixture.ResetTransactionsAsync();
        using var client = fixture.ApiFactory.CreateClient();
        var request = new TransactionRequest(new DateOnly(2026, 4, 1), 1, 1, "[TEST] Exact amount", "precision check", -1234.123456789m);

        var createResponse = await client.PostAsJsonAsync("/api/v1/transactions", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.ETag);

        var created = await createResponse.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.NotNull(created);
        Assert.Equal(request.Amount, created.Amount);
        Assert.NotEqual(Guid.Empty, created.SyncId);

        var updateRequest = new TransactionRequest(request.Date, request.AccountId, request.CategoryId, "[TEST] Edited", request.Memo, request.Amount);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/transactions/{created.Id}")
        {
            Content = JsonContent.Create(updateRequest)
        };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", "\"99\"");

        var staleResponse = await client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
        var problem = await staleResponse.Content.ReadAsStringAsync();
        Assert.Contains("transaction_version_conflict", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAndDeleteRequireIfMatch()
    {
        await fixture.ResetTransactionsAsync();
        using var client = fixture.ApiFactory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/transactions", new TransactionRequest(
            new DateOnly(2026, 4, 2), 1, 1, "[TEST] ETag required", null, -10m));
        var created = await createResponse.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/transactions/{created.Id}", new TransactionRequest(
            created.Date, created.AccountId, created.CategoryId, "[TEST] Changed", null, -10m));
        var deleteResponse = await client.DeleteAsync($"/api/v1/transactions/{created.Id}");

        Assert.Equal((HttpStatusCode)428, updateResponse.StatusCode);
        Assert.Equal((HttpStatusCode)428, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task StrongEtagAllowsUpdateAndDelete()
    {
        await fixture.ResetTransactionsAsync();
        using var client = fixture.ApiFactory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/transactions", new TransactionRequest(
            new DateOnly(2026, 4, 2), 1, 1, "[TEST] Full write flow", null, -10m));
        var created = await createResponse.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.NotNull(created);
        Assert.Equal("\"1\"", createResponse.Headers.ETag?.Tag);

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/transactions/{created.Id}")
        {
            Content = JsonContent.Create(new TransactionRequest(created.Date, created.AccountId, created.CategoryId,
                "[TEST] Updated write flow", null, -10m))
        };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("\"2\"", updateResponse.Headers.ETag?.Tag);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/transactions/{created.Id}");
        deleteRequest.Headers.TryAddWithoutValidation("If-Match", "\"2\"");
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.GetAsync($"/api/v1/transactions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task ActivitySummaryUsesFullRangeAndSearchEscapesLikeWildcards()
    {
        await fixture.ResetTransactionsAsync();
        using var client = fixture.ApiFactory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/transactions", new TransactionRequest(
            new DateOnly(2026, 4, 3), 1, 1, "[TEST] 100% Market", "wildcard % is literal", -25.50m));
        await client.PostAsJsonAsync("/api/v1/transactions", new TransactionRequest(
            new DateOnly(2026, 4, 4), 1, 3, "[TEST] Pay", null, 100.25m));

        var filtered = await client.GetFromJsonAsync<PageResponse<TransactionResponse>>(
            "/api/v1/transactions?search=%25&from=2026-04-01&to=2026-04-30&pageSize=1");
        var activity = await client.GetFromJsonAsync<ActivityResponse>(
            "/api/v1/reports/activity?from=2026-04-01&to=2026-04-30&accountId=1");

        Assert.Equal(1, filtered!.TotalCount);
        Assert.Equal("[TEST] 100% Market", filtered.Items.Single().Payee);
        Assert.Equal(100.25m, activity!.IncomeTotal);
        Assert.Equal(25.50m, activity.ExpenseTotal);
        Assert.Equal(74.75m, activity.NetActivity);
        Assert.Equal(2, activity.TransactionCount);
    }

    [Fact]
    public async Task SuccessfulTransactionWriteAddsEventToTheTransactionalOutbox()
    {
        await fixture.ResetTransactionsAsync();
        using var client = fixture.ApiFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/transactions", new TransactionRequest(
            new DateOnly(2026, 4, 5), 1, 1, "[TEST] Event receipt", null, -12.34m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var eventTypes = await fixture.GetOutboxEventTypesAsync();
        Assert.Contains("transaction.created", eventTypes);
    }
}
