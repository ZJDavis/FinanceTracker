# FinanceTracker

FinanceTracker is a Windows WPF/MVVM personal-finance demo with two data modes: the existing local SQLite store and an opt-in ASP.NET Core API backed by PostgreSQL. It is designed as a portfolio project covering desktop UI, REST design, relational persistence, optimistic concurrency, Docker Compose, and Kafka event processing.

This is a local portfolio demo, not a banking or production finance service. Use only synthetic records. The API and Compose stack bind to localhost/private networks by default and include no authentication.

## Current features

- Add, edit, and delete transactions with draft-based Save/Cancel behavior.
- Filter transactions by account, month, and payee/memo search.
- Keep the current SQLite database as the default and preserve existing local data.
- Opt into the API data mode for server-side filtering, paging, summaries, validation, and ETag concurrency.
- Store API transactions in PostgreSQL using checked-in EF Core migrations.
- Emit transaction change events through a PostgreSQL outbox to Kafka; consume them with an idempotent receipt store.
- Seed predictable synthetic accounts, categories, and multi-month transactions.

## Start the backend

Prerequisites: .NET 10 SDK, Docker Desktop/Engine with Docker Compose, and PowerShell. The WPF client runs on Windows; Docker runs only the API, PostgreSQL, and Kafka broker.

From the repository root:

```powershell
./scripts/backend.ps1 up
```

The script generates an ignored local `.env` with a random PostgreSQL password, validates Compose, builds the API image, starts PostgreSQL and Kafka, waits for their health checks, then waits for `/health/ready`. No database port is published. The API is published at `http://127.0.0.1:5080`; Swagger is at `http://127.0.0.1:5080/swagger`.

Useful commands:

```powershell
./scripts/backend.ps1 status
./scripts/backend.ps1 logs
./scripts/backend.ps1 check
./scripts/backend.ps1 down
```

`down` stops containers and keeps the PostgreSQL volume. To delete the local demo database volume, run `docker compose down --volumes` from the repository root.

## Run the WPF app against the API

SQLite remains the default and is not uploaded when data mode changes. To open the desktop app in API mode:

```powershell
$env:FINANCETRACKER_DATA_MODE = 'api'
$env:FINANCETRACKER_API_BASE_URL = 'http://127.0.0.1:5080/'
dotnet run --project FinanceTracker.Wpf.csproj
```

Close and reopen the app to change modes. Clear `FINANCETRACKER_DATA_MODE` or set it to `sqlite` to return to the local store. The SQLite database remains under `%LOCALAPPDATA%\FinanceTracker\finance.db`.

## Kafka demo

The API writes a transaction change and its outbox event in one PostgreSQL transaction. A hosted publisher sends the event to `finance.transaction-events.v1`; a hosted consumer stores a receipt by event ID before committing its Kafka offset. Delivery is at least once, so the consumer is idempotent. A broker outage does not roll back an already committed finance transaction.

After creating a transaction, view consumed event metadata at `/api/v1/events/receipts`. The local broker is a single KRaft node with no high availability or broker authentication. See [docs/kafka.md](docs/kafka.md) for the event contract and limitations.

## API and project layout

- `FinanceTracker.Core`: existing shared finance models and SQLite store abstraction.
- `FinanceTracker.Application`: API contracts, use-case boundaries, and the desktop async client boundary.
- `FinanceTracker.Data`: the existing SQLite implementation and the WPF HTTP client.
- `FinanceTracker.Data.Postgres`: EF Core context, PostgreSQL persistence, migrations, and synthetic seed data.
- `FinanceTracker.Api`: versioned REST endpoints, health checks, Kafka publisher/consumer, and ProblemDetails.
- `FinanceTracker.Wpf`: existing desktop app with configurable SQLite/API mode.
- `Tests/Api.Integration`: HTTP and PostgreSQL integration tests using Testcontainers.

The API supports accounts, categories, transaction filtering and pagination, create/update/delete, activity summaries, and ETag-based optimistic concurrency. The full endpoint list is in [docs/api.md](docs/api.md); sample calls are in [FinanceTracker.Api/requests.http](FinanceTracker.Api/requests.http).

## Import the ZD Enterprises sample ledger

The repository includes a synthetic small-business ledger from October 1, 2025 through November 23, 2026. It includes four monthly client payments totaling $100,000, five individual weekly payroll entries at $1,800 each, estimated employer payroll taxes, recurring office and software costs, and intermittent equipment, travel, training, security, and tax expenses. Employer and business-tax amounts are illustrative planning estimates, not tax guidance.

To add the transactions to the default local SQLite database:

```powershell
.\scripts\import-zd-enterprises.ps1
```

To target a separate database file:

```powershell
.\scripts\import-zd-enterprises.ps1 -DatabasePath .\data\demo\zd-enterprises.db
```

The importer creates the `ZD Enterprises Operating` account and required business categories when missing. Each row has a deterministic `SyncId`; importing the same dataset again skips matching rows and will not duplicate them. Dataset source: [Generate-ZdEnterprisesDataset.ps1](data/demo/Generate-ZdEnterprisesDataset.ps1); generated records: [zd-enterprises-transactions.json](data/demo/zd-enterprises-transactions.json).

To try the in-app CSV importer, start FinanceTracker and choose **Import CSV**, then select [zd-enterprises-transactions.csv](data/demo/zd-enterprises-transactions.csv). Review the validation and duplicate warnings, select rows, and confirm. The sample account and categories are available in both local SQLite and the demo API. See [docs/csv-import.md](docs/csv-import.md) for the file format and import behavior.

## Current backend follow-up

The backend companion (ASP.NET Core API, PostgreSQL, Kafka outbox/consumer, Docker Compose, and the PowerShell helper) and the CSV transaction import feature are in the repository. Synchronizing offline SQLite transactions with PostgreSQL, resolving same-record conflicts, and checking for convergence when the app closes are still TODO items; the sync behavior in the design document is the target design, not an implemented feature.

## Build and test

```powershell
dotnet restore FinanceTracker.sln
dotnet build FinanceTracker.sln --configuration Release
dotnet test Tests/Unit/FinanceTracker.UnitTests.csproj
dotnet test Tests/Api.Integration/FinanceTracker.Api.IntegrationTests.csproj
dotnet run --project Tests/FinanceTracker.Regression.csproj
```

The API integration tests start a temporary PostgreSQL container, so Docker must be running. `./verify.ps1` runs the solution build and all test/regression suites.

## Architecture and security notes

See [docs/architecture.md](docs/architecture.md), [docs/adr/0001-backend-shape.md](docs/adr/0001-backend-shape.md), and [FINANCETRACKER_BACKEND_DESIGN.md](FINANCETRACKER_BACKEND_DESIGN.md). The API is unauthenticated and intended for loopback use only. Any shared or public deployment needs authentication, authorization, TLS, secret management, and a privacy/security review first. Never use this demo with real personal financial data.

`docs/performance.md` lists reproducible PostgreSQL query-plan investigations. It will be updated with measured plans after the local stack is exercised; it does not claim uncollected performance results.
