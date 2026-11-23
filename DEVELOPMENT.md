# Development

Open `FinanceTracker.sln` in Visual Studio 2022 or use the .NET 10 SDK from PowerShell. The WPF app targets `net10.0-windows`; the API, application, and PostgreSQL projects target `net10.0`.

## Local API stack

Start Docker Desktop/Engine, then from the repository root run:

```powershell
./scripts/backend.ps1 up
```

The first run creates an ignored `.env` file with a random local PostgreSQL password, builds the API container, starts PostgreSQL and Kafka, applies migrations, and seeds synthetic demo data. The API binds to `127.0.0.1:5080`. PostgreSQL and Kafka are reachable only from the Compose network.

Use `./scripts/backend.ps1 status`, `logs`, `check`, or `down` to manage the stack. `down` preserves the PostgreSQL volume. `docker compose down --volumes` removes that local demo volume.

## Desktop data modes

SQLite remains the default and uses `%LOCALAPPDATA%\FinanceTracker\finance.db`.

To launch the app against the local API:

```powershell
$env:FINANCETRACKER_DATA_MODE = 'api'
$env:FINANCETRACKER_API_BASE_URL = 'http://127.0.0.1:5080/'
dotnet run --project FinanceTracker.Wpf.csproj
```

Clear `FINANCETRACKER_DATA_MODE` or set it to `sqlite` to return to SQLite. Changing modes never copies, uploads, or merges data.

### Importing transactions

Choose **Import CSV** in the app, select a transaction CSV, review validation and duplicate warnings, then confirm the rows to add. The generated ZD Enterprises sample can be selected from `data/demo/zd-enterprises-transactions.csv`. The format and current limitations are documented in [docs/csv-import.md](docs/csv-import.md).

SQLite/PostgreSQL database synchronization is still TODO. API mode and SQLite mode currently operate on their selected database independently; the sync/conflict design in `FINANCETRACKER_BACKEND_DESIGN.md` is planned behavior, not implemented behavior.

## Build and checks

```powershell
dotnet restore FinanceTracker.sln
dotnet build FinanceTracker.sln --configuration Release
dotnet test Tests/Unit/FinanceTracker.UnitTests.csproj
dotnet test Tests/Api.Integration/FinanceTracker.Api.IntegrationTests.csproj
dotnet run --project Tests/FinanceTracker.Regression.csproj
```

The API integration test project uses Testcontainers and needs Docker running. `./verify.ps1` runs the solution build, unit tests, PostgreSQL/API integration tests, and existing WPF regression checks.

For API requests, see `FinanceTracker.Api/requests.http`. For architecture, event delivery guarantees, and security limitations, see `docs/architecture.md`, `docs/kafka.md`, and `SECURITY.md`.
