using FinanceTracker.Api.Kafka;
using FinanceTracker.Api.Middleware;
using FinanceTracker.Application.Contracts;
using FinanceTracker.Data.Postgres;
using FinanceTracker.Data.Postgres.Seeding;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("FinanceTracker")
    ?? throw new InvalidOperationException("ConnectionStrings:FinanceTracker must be configured.");

builder.Services.AddDbContext<FinanceDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));

if (builder.Configuration.GetValue("Kafka:Enabled", true))
{
    builder.Services.AddSingleton<KafkaProducer>();
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddHostedService<KafkaEventConsumer>();
}

var app = builder.Build();

app.UseMiddleware<FinanceExceptionMiddleware>();

// Applying migrations on startup keeps the local demo to one command and is
// safe because Compose starts the API only after PostgreSQL is healthy.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await database.Database.MigrateAsync();
    await DemoDataSeeder.SeedAsync(database);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
    .WithName("Liveness");

app.MapGet("/health/ready", async (FinanceDbContext database, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database is unavailable.");

        var pendingMigrations = await database.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database migrations are pending.");

        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database is unavailable.");
    }
}).WithName("Readiness");

var api = app.MapGroup("/api/v1");

api.MapGet("/accounts", async (bool? includeInactive, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetAccountsAsync(includeInactive ?? false, cancellationToken)))
    .WithName("GetAccounts");

api.MapGet("/accounts/{id:int}", async (int id, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetAccountAsync(id, cancellationToken)))
    .WithName("GetAccount");

api.MapGet("/categories", async (bool? includeInactive, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetCategoriesAsync(includeInactive ?? false, cancellationToken)))
    .WithName("GetCategories");

api.MapGet("/categories/{id:int}", async (int id, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetCategoryAsync(id, cancellationToken)))
    .WithName("GetCategory");

api.MapGet("/transactions", async (
    int? accountId,
    int? categoryId,
    DateOnly? from,
    DateOnly? to,
    string? search,
    IFinanceService finance,
    CancellationToken cancellationToken,
    string sort = "date",
    string direction = "desc",
    int page = 1,
    int pageSize = 25) =>
{
    var query = new TransactionQuery(accountId, categoryId, from, to, search, sort, direction, page, pageSize);
    return Results.Ok(await finance.GetTransactionsAsync(query, cancellationToken));
}).WithName("GetTransactions");

api.MapGet("/transactions/{id:int}", async (int id, IFinanceService finance, HttpResponse response, CancellationToken cancellationToken) =>
{
    var transaction = await finance.GetTransactionAsync(id, cancellationToken);
    response.Headers.ETag = ToETag(transaction.Version);
    return Results.Ok(transaction);
}).WithName("GetTransaction");

api.MapPost("/transactions", async (TransactionRequest request, IFinanceService finance, HttpResponse response, CancellationToken cancellationToken) =>
{
    var transaction = await finance.CreateTransactionAsync(request, cancellationToken);
    response.Headers.ETag = ToETag(transaction.Version);
    return Results.Created($"/api/v1/transactions/{transaction.Id}", transaction);
}).WithName("CreateTransaction");

api.MapPut("/transactions/{id:int}", async (int id, TransactionRequest request, HttpRequest httpRequest, IFinanceService finance, HttpResponse response, CancellationToken cancellationToken) =>
{
    if (!TryReadExpectedVersion(httpRequest, out var expectedVersion, out var problem)) return problem!;

    var transaction = await finance.UpdateTransactionAsync(id, expectedVersion, request, cancellationToken);
    response.Headers.ETag = ToETag(transaction.Version);
    return Results.Ok(transaction);
}).WithName("UpdateTransaction");

api.MapDelete("/transactions/{id:int}", async (int id, HttpRequest httpRequest, IFinanceService finance, CancellationToken cancellationToken) =>
{
    if (!TryReadExpectedVersion(httpRequest, out var expectedVersion, out var problem)) return problem!;

    await finance.DeleteTransactionAsync(id, expectedVersion, cancellationToken);
    return Results.NoContent();
}).WithName("DeleteTransaction");

api.MapGet("/reports/activity", async (DateOnly from, DateOnly to, int? accountId, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetActivityAsync(from, to, accountId, cancellationToken)))
    .WithName("GetActivityReport");

api.MapGet("/events/receipts", async (int? limit, IFinanceService finance, CancellationToken cancellationToken) =>
    Results.Ok(await finance.GetRecentEventReceiptsAsync(limit ?? 20, cancellationToken)))
    .WithName("GetEventReceipts");

app.Run();

static string ToETag(int version) => $"\"{version}\"";

static bool TryReadExpectedVersion(HttpRequest request, out int version, out IResult? problem)
{
    version = 0;
    if (!request.Headers.TryGetValue("If-Match", out var values) || values.Count == 0)
    {
        problem = Results.Problem(statusCode: 428, title: "If-Match is required.", extensions: new Dictionary<string, object?> { ["code"] = "if_match_required" });
        return false;
    }

    var value = values[0]?.Trim();
    if (value is null || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) || value.Length < 3 ||
        value[0] != '"' || value[^1] != '"' || !int.TryParse(value[1..^1], out version) || version < 1)
    {
        problem = Results.Problem(statusCode: 400, title: "If-Match must contain a strong transaction version ETag.", extensions: new Dictionary<string, object?> { ["code"] = "if_match_invalid" });
        return false;
    }

    problem = null;
    return true;
}

public partial class Program { }
