# FinanceTracker Backend Companion — Implementation Design

**Status:** Ready for implementation planning  
**Audience:** Engineering team  
**Purpose:** Add a portfolio-quality ASP.NET Core backend and PostgreSQL storage to FinanceTracker while retaining its existing Windows desktop experience and local SQLite mode.

## 1. Executive summary

Build an ASP.NET Core REST API and PostgreSQL persistence layer that can serve the existing FinanceTracker WPF application. Keep the work in the FinanceTracker repository as a single deployable backend with clear internal module boundaries (a modular monolith). Add an opt-in API data mode to the desktop app so the existing UI can demonstrate local SQLite and remote API operation. Demonstrate a realistic event-driven workflow with Kafka, a transactional outbox, and an idempotent consumer.

The result should be runnable locally with Docker Compose, use synthetic financial data, include automated unit and PostgreSQL integration tests, and have a CI pipeline. It should demonstrate practical backend skills relevant to .NET service development: REST, PostgreSQL schema and query design, migrations, validation, concurrency, containerized development, CI, and maintainable documentation.

This is a portfolio demo, not a real banking or production finance service. The API must bind to localhost by default and must not be used with real personal financial data.

## 2. Existing application and constraints

The current public project is [FinanceTracker](https://github.com/ZJDavis/FinanceTracker). It is a C# WPF/MVVM desktop app. Its solution has `FinanceTracker.Core`, `FinanceTracker.Data`, a WPF project, and tests. The data layer currently persists accounts, categories, and transactions in a local SQLite database under the current user's local application data directory. The data model includes account and category relationships, `DateOnly` dates, decimal amounts, and a transaction version used for optimistic concurrency. The desktop view model currently calls a synchronous store and performs filtering in the client.

The backend must extend this design rather than discard it:

- Keep the existing local SQLite mode as the default and preserve existing user data.
- Add automatic backend detection with a persisted API base URL and offline SQLite fallback.
- Synchronize transaction data in both directions while preserving SQLite as the offline cache.
- Detect concurrent edits to the same transaction and ask the user which version to keep before overwriting either copy.
- Preserve the existing transaction sign convention: expenses are negative, income is positive, and net activity is their sum.
- Preserve decimal precision. Do not convert amounts to floating point.
- Preserve optimistic concurrency. Stale edits must not silently overwrite newer data.
- Refactor the desktop data boundary to asynchronous operations so HTTP calls do not block the WPF UI thread.
- Keep the code understandable as a portfolio project. Do not introduce multiple services or infrastructure solely to make the diagram look more complex.

## 3. Goals and non-goals

### Goals

1. Provide a documented, versioned REST API for accounts, categories, transactions, and activity summaries.
2. Store API-mode data in PostgreSQL using version-controlled EF Core migrations.
3. Connect the existing WPF application to the API without removing SQLite mode.
4. Demonstrate server-side filtering, pagination, validation, concurrency handling, and query performance investigation.
5. Make the project straightforward to run and review with Docker Compose and a single verification command.
6. Build a useful test suite around business rules, API behavior, migrations, and PostgreSQL-specific behavior.
7. Document architecture choices and provide a small, deterministic demo dataset.
8. Show transaction events flowing from PostgreSQL through Kafka and into an idempotent consumer receipt store.

### Non-goals for the first release

- Public internet deployment or multi-user hosting.
- Authentication, authorization, user accounts, tenant isolation, or synchronization between users.
- Bank connections, payment processing, real money movement, or handling real financial records.
- Replacing the WPF interface with a web interface.
- Splitting the application into microservices.
- Account and category editing screens. API-mode accounts and categories are read-only seeded reference data in the first release.
- Kubernetes and Helm. CSV transaction import is implemented as a desktop preview-and-confirm workflow; see [docs/csv-import.md](docs/csv-import.md). Kafka is included as a bounded local event-flow demonstration.

### Implementation status

The repository now includes the backend companion (ASP.NET Core API, PostgreSQL persistence, Kafka outbox/consumer, Compose, and `scripts/backend.ps1`) and a desktop CSV transaction importer. SQLite-to-PostgreSQL synchronization is not implemented yet. The requirements below describe the intended sync behavior and remain the TODO design: bidirectional change propagation, stable-reference matching, row-scoped locking, conflict prompts, deletion tombstones, and close-time convergence checks.

## 4. Decisions the implementation team should treat as fixed

| Topic | Decision |
|---|---|
| Repository | Extend the existing FinanceTracker repository (monorepo). |
| Architecture | One ASP.NET Core API deployable, organized by feature/module. |
| Framework | Upgrade the solution to .NET 10 LTS. The project currently targets .NET 8; .NET 8 support ended on 2026-11-10, while .NET 10 is the active LTS line. See the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy). |
| API | ASP.NET Core REST API with explicit `/api/v1` routes and OpenAPI documentation. |
| Database | PostgreSQL, accessed through EF Core and the Npgsql provider. |
| Desktop integration | Use persisted settings and automatic API detection. Keep SQLite available as the offline cache and fallback. |
| Backend URL | Persist the API base URL in `%LOCALAPPDATA%\\FinanceTracker\\settings.json`; default to `http://127.0.0.1:5080/` for the local Compose demo. |
| Startup | Open SQLite promptly, probe `/health/ready` in the background, and synchronize when the configured API is reachable. If unavailable, prompt to retry, continue offline, or exit. |
| Synchronization | Track a stable GUID and last-synchronized snapshot per transaction. Push local-only changes and pull server-only changes. A same-record divergent change must be resolved by the user before either copy is overwritten. |
| Background sync | Keep the WPF UI responsive. Lock only transactions participating in the current sync batch; unaffected rows remain usable. Protect writes with local versions and API ETags. |
| Closing | When online, finish a sync pass and verify matching transaction state before closing. If conflicts or failures remain, show them and let the user return to the app or close with changes queued. While offline, preserve pending local changes for the next connection. |
| Amounts | C# `decimal`, JSON number, and PostgreSQL `numeric`; never use `float`/`double` for money. |
| Concurrency | Keep a monotonically increasing transaction `Version`; expose it as an ETag and require it on edits/deletes. |
| Local operation | Docker Compose for PostgreSQL, API, and one Kafka KRaft broker. Only the API is published on loopback; PostgreSQL and Kafka remain on the private Compose network. |
| CI | GitHub Actions, matching the repository host; build, format, test, PostgreSQL integration test, and container build. |
| Demo data | Deterministic synthetic data only. No personal or imported records. |
| Kafka | One local KRaft broker in Compose; transaction events use a PostgreSQL outbox, an in-process publisher, and an idempotent in-process consumer. Kafka is a portfolio demonstration and is not required for finance reads or writes to succeed. |

## 5. Architecture

```text
                         ┌─────────────────────────┐
                         │ FinanceTracker WPF app  │
                         │ async data-client port  │
                         └────────────┬────────────┘
                                      │ selected data mode
                    ┌─────────────────┴─────────────────┐
                    │                                   │
        ┌───────────▼───────────┐           ┌───────────▼────────────┐
        │ SQLite data client    │           │ HTTP API data client   │
        │ existing local store  │           │ JSON + ETag handling   │
        └───────────────────────┘           └───────────┬────────────┘
                                                        │ HTTP / JSON
                                            ┌───────────▼────────────┐
                                            │ FinanceTracker.Api     │
                                            │ v1 endpoints/modules   │
                                            └───────────┬────────────┘
                                                        │ EF Core
                                            ┌───────────▼────────────┐
                                            │ PostgreSQL             │
                                            └────────────────────────┘
```

The API should be a modular monolith: one process and one deployment with code organized around Accounts, Categories, Transactions, Reports, and event handling. The application/service layer owns rules and use cases; the persistence layer owns database access; the API layer owns HTTP concerns. The outbox publisher and Kafka consumer run as hosted workers in the API process. This gives the demo a real producer/consumer path without splitting it into services.

When a transaction is created, updated, or deleted, write the transaction change and an outbox row in the same PostgreSQL transaction. A hosted publisher sends unsent outbox rows to `finance.transaction-events.v1`. A second hosted worker consumes the topic and stores a receipt keyed by event ID before committing the Kafka offset. If a publish succeeds but marking the outbox row fails, the publisher can send a duplicate; the unique receipt key makes the consumer idempotent. Delivery is at least once, not exactly once. Kafka failure must not roll back a transaction already committed to PostgreSQL. The receipt endpoint is a read-only demo surface and must not return transaction payloads.

### Desktop synchronization and conflict resolution

The desktop app keeps SQLite as its offline cache. Settings are stored per Windows user in `%LOCALAPPDATA%\\FinanceTracker\\settings.json`; they include the API base URL and sync preferences, not credentials. The local Docker endpoint `http://127.0.0.1:5080/` is the default. The URL can be changed to a stable LAN or hosted address.

SQLite opens first. A background readiness probe checks the configured URL. If reachable, the app enters connected mode and starts synchronization without blocking unrelated UI work. If unavailable, it clearly reports that state and offers retry, offline mode, or exit. A failed probe never discards local edits.

Each transaction has an immutable client-generated `SyncId` GUID. SQLite stores a last-synchronized snapshot and sync metadata for each `SyncId`; PostgreSQL stores the same GUID with a unique constraint and retains deletion tombstones. Integer transaction IDs remain database-local. Reference records are matched using stable reference keys, never by assuming integer IDs match.

For each transaction, synchronization compares the current SQLite row, current PostgreSQL row, and last-synchronized snapshot. A change on only one side is copied to the other. If both sides changed to identical values, the shared snapshot advances. If both sides changed differently, both versions are retained and that transaction is locked while the user chooses which version to keep. The selected version is applied to the other database only after that choice. Deletions use tombstones so a deleted record is not recreated on the next pull.

Synchronization is idempotent and uses bounded batches. Local row changes and sync metadata commit together; server changes and sync metadata commit together. Only rows participating in the current batch are marked as synchronizing and made read-only; unaffected transactions remain available. Local optimistic versions and server ETags detect edits that race with synchronization. On close while online, the app completes a sync pass and verifies convergence. Unresolved conflicts or connection failures are reported, and the user may return to the app or close with pending work retained. Offline close preserves local changes and never claims PostgreSQL is current.

The first version synchronizes transactions and their deletion state. Accounts and categories remain shared reference data. The API exposes stable reference keys for mapping. A missing or ambiguous mapping blocks only the affected transaction and is reported for resolution; synchronization must never guess an account or category.

These synchronization behaviors are design requirements only; the current application does not yet synchronize SQLite and PostgreSQL. Do not describe startup sync, conflict resolution, row locks, tombstones, or close-time verification as implemented until those features are built and exercised.

### CSV transaction import

The desktop application supports a user-selected CSV file in either active data mode. It validates the documented date/amount/required-field format, matches existing account and category names, checks category kind against amount sign, and identifies likely content or `SyncId` duplicates. A preview lets the user include or exclude valid rows; duplicates start unchecked and invalid rows cannot be included. Selected rows are saved through the active SQLite or HTTP client. API imports preserve the provided `SyncId` and publish the normal transaction-created outbox event. The included ZD Enterprises CSV exercises the workflow. The first version does not create reference data, offer arbitrary column mapping, provide batch rollback, or synchronize the two databases. See [docs/csv-import.md](docs/csv-import.md).

### Suggested solution layout

Names may be adjusted to follow established repository conventions, but keep these responsibilities separate:

```text
FinanceTracker.sln
├── FinanceTracker.Core/                 # Existing domain models and shared rules
├── FinanceTracker.Data/                 # Existing SQLite implementation
├── FinanceTracker.Application/          # Use cases, validation, query/result contracts
├── FinanceTracker.Data.Postgres/        # DbContext, EF configurations, migrations
├── FinanceTracker.Api/                  # HTTP endpoints, composition root, OpenAPI
├── FinanceTracker.Wpf/                  # Existing WPF app plus HTTP client/data-mode UI
└── Tests/
    ├── Unit/
    ├── Api.Integration/
    └── FinanceTracker.Regression.csproj # Existing regression checks
```

If the current repository's project boundaries make a new Application project unnecessary, the team may keep application use cases in Core, provided HTTP and EF Core dependencies do not leak into Core. Record the final choice in a short architecture decision record (ADR).

### Dependency direction

- `Core` contains domain concepts and rules; it must not reference ASP.NET Core, EF Core, Npgsql, WPF, or HTTP.
- `Application` references Core and defines use cases and persistence-facing abstractions where needed.
- `Data.Postgres` implements persistence using EF Core/Npgsql.
- `Api` composes the application and data implementations and translates HTTP requests/responses.
- WPF depends on its data-client abstraction and the SQLite/HTTP implementations; it does not reference the API's EF Core project.

## 6. Domain and persistence model

Use the existing Account, Category, and Transaction concepts. Keep wire DTOs separate from EF entities and avoid exposing database entities directly from controllers.

### Accounts

- `Id`: integer or UUID; for compatibility with the current local model, use integer IDs in API v1.
- `Name`: required, trimmed, maximum 100 characters.
- `Type`: required, maximum 50 characters (examples: Checking, Credit Card).
- `OpeningBalance`: decimal/numeric.
- `IsActive`: boolean, default true; inactive accounts remain visible for historical transactions but are not valid for new transaction writes.

### Categories

- `Id`: integer.
- `Name`: required, trimmed, maximum 100 characters.
- `Kind`: `Income`, `Expense`, or `Transfer`.
- `IsActive`: boolean, default true, with the same historical-use behavior as accounts.

### Transactions

- `Id`: generated integer.
- `Version`: integer, starts at 1 and increments after every successful update.
- `Date`: calendar date, stored as PostgreSQL `date`.
- `AccountId`: required foreign key.
- `CategoryId`: required foreign key.
- `Payee`: required, trimmed, maximum 200 characters.
- `Memo`: optional, maximum 2,000 characters.
- `Amount`: required decimal stored as `numeric`; expenses are negative and income positive.
- `CreatedAtUtc`, `UpdatedAtUtc`: UTC timestamps for auditability.

Validation must reject an amount of zero, an invalid account/category, an inactive account/category on new writes, a category kind that conflicts with the amount sign (Income must be positive; Expense must be negative), and an invalid or out-of-range date. `Transfer` is reserved in the existing model but is not fully modeled as a paired-account transfer in v1; permit it only if the current app's behavior already relies on it, otherwise document it as unavailable for API writes until a paired transfer use case is designed. Do not silently model a transfer as an ordinary income or expense.

### Database constraints and indexes

- Foreign keys from transactions to accounts and categories.
- Check constraints for nonzero amount and positive version.
- Unique constraint for account names and category names, case-insensitive if that can be implemented reliably in the selected PostgreSQL collation. If case-insensitive uniqueness is not portable in the chosen setup, normalize names in application validation and document the limitation.
- Index transactions on `(account_id, date DESC, id DESC)` for the main account/date history query.
- Index transactions on `(category_id, date DESC)` for category reporting.
- Add further indexes only when query-plan review or measurements show a need. Do not add a speculative index for every filter.
- A migration must create all constraints/indexes and a clean database must be buildable from migrations alone.

Use an unconstrained or sufficiently broad PostgreSQL `numeric` mapping that preserves the current application's accepted decimal precision. Confirm the EF Core mapping and JSON round-trip behavior with tests before finalizing it. Do not round amounts to two decimal places unless the current app explicitly imposes that rule.

## 7. API contract

All endpoints use `/api/v1`. JSON uses camelCase. Dates use ISO 8601 calendar-date strings (`YYYY-MM-DD`); timestamps use UTC ISO 8601. Amounts are JSON numbers that deserialize to `decimal`. Return consistent `ProblemDetails` for errors and never return database details or stack traces.

### Reference data

| Method and route | Behavior |
|---|---|
| `GET /api/v1/accounts` | List active accounts by default; support `includeInactive=true` for historical selection. |
| `GET /api/v1/accounts/{id}` | Return one account or 404. |
| `GET /api/v1/categories` | List active categories by default; support `includeInactive=true`. |
| `GET /api/v1/categories/{id}` | Return one category or 404. |

API v1 does not create or edit accounts/categories. Seed data is versioned and deterministic; the seed operation must be idempotent and must not reset user-created transactions.

### Transactions

| Method and route | Behavior |
|---|---|
| `GET /api/v1/transactions` | List transactions with filtering, sorting, and pagination. |
| `GET /api/v1/transactions/{id}` | Return one transaction with its version/ETag or 404. |
| `POST /api/v1/transactions` | Validate and create a transaction; return 201, the resource URL, and ETag. |
| `PUT /api/v1/transactions/{id}` | Replace editable fields; require `If-Match` with the version ETag. Return 200 and the new ETag. |
| `DELETE /api/v1/transactions/{id}` | Require `If-Match`. Return 204 on success. |

`GET /transactions` query parameters:

- `accountId` (optional)
- `categoryId` (optional)
- `from` and `to` (inclusive ISO dates; reject `from > to`)
- `search` (optional substring search over payee and memo, trimmed, maximum 100 characters)
- `sort` (`date`, `amount`, or `payee`; default `date`)
- `direction` (`asc` or `desc`; default `desc`)
- `page` (1-based, default 1)
- `pageSize` (default 25, maximum 100)

Return an envelope with `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. Add a stable `id` tie-breaker to sorting so pages do not shuffle when values match. Reject unsupported sort keys and invalid paging values with 400.

Example transaction representation:

```json
{
  "id": 1042,
  "version": 3,
  "date": "2026-10-15",
  "accountId": 1,
  "categoryId": 2,
  "payee": "Example Market",
  "memo": "Weekly groceries",
  "amount": -42.75
}
```

On update/delete, a stale ETag returns `412 Precondition Failed` with a stable error code such as `transaction_version_conflict`. A missing resource returns 404. Do not silently retry a write with the latest version because that would overwrite a user's changes.

### Activity summary

`GET /api/v1/reports/activity?from=YYYY-MM-DD&to=YYYY-MM-DD&accountId=...`

Return totals for the full matching date range, not just the current page:

```json
{
  "from": "2026-10-01",
  "to": "2026-10-31",
  "accountId": 1,
  "incomeTotal": 2500.00,
  "expenseTotal": 840.30,
  "netActivity": 1659.70,
  "transactionCount": 22
}
```

`incomeTotal` is positive, `expenseTotal` is a positive magnitude for display, and `netActivity` is the signed sum of transactions. Aggregate in PostgreSQL rather than loading all rows into API memory.

### Operational endpoints

- `GET /health/live`: process is alive; no database dependency.
- `GET /health/ready`: database can be reached and migrations/schema are usable.
- OpenAPI/Swagger UI is enabled in Development only. A checked-in `.http` request file or equivalent examples must cover the main endpoint flows.

### HTTP status and error mapping

| Status | Use |
|---|---|
| 200 | Successful read/update. |
| 201 | Successful create, with `Location` and ETag headers. |
| 204 | Successful delete. |
| 400 | Invalid query, malformed request, or domain validation failure. |
| 404 | Requested account/category/transaction does not exist. |
| 412 | Supplied transaction ETag/version is stale. |
| 428 | Required `If-Match` header is missing on an update/delete. |
| 409 | Unique constraint or other conflict not represented by the ETag check. |
| 500 | Unexpected failure, with a correlation/trace identifier but no internal details. |
| 503 | Readiness/dependency failure where appropriate. |

## 8. Desktop application changes required

These changes are part of the backend delivery; a standalone API that cannot be demonstrated through FinanceTracker is incomplete.

### Data mode and configuration

- Add an explicit `DataMode` setting with `Sqlite` as the default and `Api` as the remote option.
- Read API base URL from configuration. Default to `http://localhost:<documented-port>` for local Docker use.
- Show the selected mode clearly in the window/status area and provide a connection status/error message in API mode.
- Never upload, import, merge, or overwrite SQLite data when the user changes modes.
- Make switching modes explicit and restart-required unless the existing app architecture can switch safely without restart. Document this behavior.

### Asynchronous data boundary

Replace the synchronous-only store use in view models with an async client abstraction. It should support initialization, reference data loading, transaction querying, create/update, delete, and summary retrieval using cancellation tokens. Adapt the SQLite store to that abstraction while retaining its existing semantics; add an HTTP implementation that maps API errors and ETags into user-facing results.

Update WPF commands and view models to:

- avoid blocking the dispatcher during API work;
- display busy state and disable duplicate save/delete actions;
- preserve the current draft if a save fails;
- retain the user's edits when a 412 conflict occurs and offer reload/discard actions;
- show a clear retry/reload affordance after a network failure;
- pass account, date, category, search, sort, and pagination filters to the server in API mode;
- update the summary from the API's full-filter aggregate, not only the currently loaded page;
- keep the current local filtering behavior in SQLite mode.

### WPF acceptance behavior

- A clean install still starts in SQLite mode and creates/opens the current local database as before.
- Existing SQLite data remains readable after the framework and interface refactor.
- In API mode, the user can list, filter, page, create, edit, delete, and see activity summaries for API data.
- Network errors and concurrency conflicts do not clear the editor draft or falsely report success.
- API mode starts with synthetic reference data and does not affect local SQLite data.

## 9. Security, privacy, and configuration

This is an unauthenticated local portfolio demo. Reduce exposure by design:

- Bind the API to loopback in Development and publish the API port in Compose as `127.0.0.1:hostPort:containerPort`.
- Keep PostgreSQL on the internal Compose network; do not publish its port by default. If developers need direct access, document an explicit local-only override.
- Use environment variables or user secrets for connection strings. Do not check credentials into source control.
- Use parameterized EF Core queries and validate all user-controlled input.
- Do not include real financial data in fixtures, screenshots, logs, or sample files.
- Do not log full memo/payee values or request bodies by default; log route, outcome, trace ID, and safe identifiers.
- Return generic server errors to clients and log exception details only to the local development log.
- State in README that non-loopback or public deployment requires authentication, authorization, TLS, secret management, and a privacy/security review before exposure.

No authentication is included in API v1 because the intended showcase is a local single-user demo. Do not imply that the API is safe for shared or public use.

## 10. Local developer and demo experience

The repository should provide a documented happy path equivalent to:

1. Install the documented .NET 10 SDK and Docker Desktop/Engine.
2. Run `./scripts/backend.ps1 up` from the repository root. It creates an ignored local `.env` file with a random database password and starts the Compose stack.
3. Compose waits for PostgreSQL and Kafka health checks before starting the API; the script waits for `/health/ready`.
4. Start FinanceTracker in API mode using a documented configuration profile/launch setting.
5. Demonstrate seeded accounts/categories, server-side filtering and paging, create/edit/delete, and the activity summary.
6. View processed event receipts at `/api/v1/events/receipts`, then stop the stack with `./scripts/backend.ps1 down`; stopping preserves the demo database volume.

Provide a deterministic seed set that includes at least two accounts, income and expense categories, transactions across multiple months, transactions with the same date/amount for stable-sort checks, and boundary dates. Mark records visibly/documentarily as demo data. Re-running startup or migrations must not duplicate the seed set or erase transactions.

## 11. Testing and quality gates

Use TDD for business rules and API behavior. Keep and update existing FinanceTracker tests and regression checks; the new backend must not make the existing desktop behavior unverified.

### Unit tests

- Transaction validation: sign/category agreement, required fields, lengths, zero amount, inactive references.
- Activity summary calculations, including refunds/negative corrections if permitted by current rules.
- Filtering parameter validation and stable sort construction.
- Mapping between API DTOs, domain models, and persistence models.
- Concurrency error mapping.

### PostgreSQL/API integration tests

Run against a real PostgreSQL instance (CI service container or Testcontainers), not EF Core's in-memory provider. Verify:

- all migrations apply to an empty database;
- seed data is idempotent;
- create/read/update/delete flows and foreign keys;
- unique constraints and validation errors;
- decimal precision round trips without float conversion or unintended rounding;
- date-only round trips and inclusive date filters;
- account/category/search filters, pagination metadata, and stable ordering;
- activity totals span all matching pages;
- stale ETag update/delete returns 412 and leaves the stored row unchanged;
- transaction writes and outbox events commit atomically; the Kafka consumer saves a receipt before committing its offset and ignores duplicate event IDs;
- parameterized search does not interpret user input as SQL.

### Desktop/regression tests

- Keep the current SQLite regression suite passing after the async abstraction change.
- Add tests for configuration selection, HTTP client error mapping, ETag handling, failed-save draft retention, and query argument construction.
- Manual smoke check both modes: clean local database, seeded API, offline API, validation failure, and stale edit.

### CI pipeline

GitHub Actions should run on pull requests and pushes to the main branch:

1. Restore packages.
2. Run `dotnet format --verify-no-changes` (or the repository's equivalent formatting gate).
3. Build the full solution in Release with nullable analysis enabled for new code.
4. Run all unit, API integration, and existing regression tests; provision PostgreSQL for integration tests. Exercise Kafka publishing and consumption through the local Compose stack.
5. Collect and publish test/coverage summaries as CI artifacts. Set an initial coverage reporting threshold for new Application/API code (target 80% line coverage); avoid gaming the percentage with trivial tests.
6. Build the API container image without pushing it.

If SonarQube/SonarCloud credentials are available, add a separate configured scan and document the quality gate. CI must still work without private Sonar credentials. The pipeline should not claim a passing gate when the scanner is skipped.

## 12. Performance and PostgreSQL demonstration

The demo should show evidence of database-aware implementation without premature tuning:

- Apply filtering, sorting, paging, and summary aggregation in SQL.
- Keep page size bounded to 100.
- Use deterministic `ORDER BY` including a unique tie-breaker.
- Add the indexes in Section 6, then document `EXPLAIN (ANALYZE, BUFFERS)` for the main account/date query and activity aggregation using a generated dataset of at least 100,000 synthetic rows.
- Record the query, relevant plan excerpt, dataset size, and environment in `docs/performance.md`.
- If substring search is measurably slow, investigate PostgreSQL trigram indexing as a follow-up and include the extension/index cost in the documentation. Do not add it before measurement.

## 13. Delivery phases

### Phase 1 — Baseline and boundaries

- Move the full solution to .NET 10 and resolve package compatibility.
- Introduce/update the asynchronous data-client boundary and retain SQLite as default.
- Keep all existing SQLite tests/regression checks passing.
- Add a short ADR documenting the modular-monolith and data-mode decisions.

### Phase 2 — API and PostgreSQL

- Add Application, Postgres, and API projects.
- Implement migrations, constraints, indexes, health endpoints, development seed data, and API contract.
- Add validation, ProblemDetails, OpenAPI, and ETag concurrency behavior.
- Implement unit and PostgreSQL integration tests alongside endpoints.

### Phase 3 — Desktop API mode

- Implement the HTTP client and configuration selection.
- Add asynchronous loading/saving, server-side filtering/paging, remote summaries, error/retry states, and concurrency conflict handling.
- Confirm all existing local SQLite workflows still work.

### Phase 4 — Packaging and portfolio polish

- Add Compose for PostgreSQL and Kafka, the API container, CI, `.http` examples, README run instructions, architecture diagram, and performance note.
- Demonstrate the outbox publisher, Kafka topic, duplicate-safe receipt consumer, and recovery after a Kafka restart.
- Run a clean-clone demonstration and document actual commands/results.
- Capture a short demo flow or screenshots only after verifying they contain synthetic data and no local personal records.

Do not start the optional stretch work until all Phase 1–4 acceptance criteria pass.

## 14. Optional follow-up portfolio extensions

These are not part of the first delivery and must not delay it:

1. **CSV statement import enhancements.** The first transaction CSV preview/import is implemented. Custom column mapping, an import batch state machine, rollback, and stronger idempotency/audit rules remain follow-up work before adding a queue.
2. **Production-grade Kafka operations.** The local first-release event flow is intentionally single-node and at-least-once. A hosted version would need broker authentication/TLS, topic provisioning and retention, dead-letter handling, consumer-lag monitoring, outbox cleanup, and deployment-specific scaling. Do not claim the local setup provides high availability or exactly-once delivery.
3. **Authentication for a hosted demo.** Add identity, authorization, user ownership, tenant scoping, secrets, TLS, and privacy requirements before any public deployment.
4. **Account/category management.** Add API write operations and UI screens with duplicate detection and deletion rules that preserve transaction history.
5. **GitLab CI mirror.** The repository currently uses GitHub, so GitHub Actions is the primary pipeline. A GitLab mirror can be added if a specific application/demo environment requires it.

## 15. Acceptance criteria / definition of done

The work is complete when all of the following are true:

- The full solution targets .NET 10 and builds in Release from a clean checkout.
- Existing SQLite mode remains the default, retains existing local data, and passes the existing regression suite.
- A developer can start PostgreSQL and the API locally using the documented Compose command.
- A clean database is created from checked-in migrations, and seed data is deterministic and idempotent.
- The API supports the documented read, create, update, delete, filtering, paging, and activity-summary contract.
- Amount precision, date behavior, constraints, and optimistic concurrency are covered by PostgreSQL integration tests.
- The WPF app can run in API mode and complete the documented demo flow without blocking the UI.
- Failed network requests and stale edits preserve the user's draft and present an actionable message.
- The API/database are not exposed beyond localhost by the default Compose configuration.
- Transaction events use an outbox and idempotent consumer receipt store; Kafka is a local demo dependency, not a source of truth for financial data.
- CI builds, formats, tests (including PostgreSQL integration tests), and builds the API container image.
- README explains architecture, local run steps, switching data modes, test commands, limitations, and security boundaries.
- `docs/performance.md` records the measured PostgreSQL query plan and dataset used.
- No feature depends on real financial data, public hosting, or a separately deployed microservice.

## 16. Documentation and handoff artifacts

Implementation should update or add:

- `README.md`: project overview, architecture, quick start, data-mode selection, API docs, tests, and security limitations.
- `docs/architecture.md`: component/dependency diagram and request flow.
- `docs/adr/0001-backend-shape.md`: modular monolith and local/remote data mode decision.
- `docs/api.md` or generated OpenAPI as the canonical API reference.
- `docs/performance.md`: reproducible query-plan investigation.
- `FinanceTracker.Api/requests.http`: sample requests for the principal flows.
- `docker-compose.yml` and API `Dockerfile`.
- `scripts/backend.ps1`: PowerShell wrapper to build/start the local stack, check readiness, view logs/status, and stop containers while preserving the demo database volume.
- `docs/kafka.md`: event contract, outbox delivery guarantees, idempotent consumer, and local demo flow.
- GitHub Actions workflow under `.github/workflows/`.

## 17. Reference material

- [FinanceTracker repository](https://github.com/ZJDavis/FinanceTracker)
- [FinanceTracker development and verification notes](https://github.com/ZJDavis/FinanceTracker/blob/master/DEVELOPMENT.md)
- [Existing SQLite persistence implementation](https://github.com/ZJDavis/FinanceTracker/blob/master/FinanceTracker.Data/SqliteFinanceStore.cs)
- [Official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
