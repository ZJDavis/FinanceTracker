# FinanceTracker API v1

The API is an unauthenticated, loopback-only portfolio demo. Do not send real financial records or expose it to a network. Development Swagger is available at `http://127.0.0.1:5080/swagger` after `scripts/backend.ps1 up`.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/health/live` | Process liveness |
| GET | `/health/ready` | PostgreSQL and migration readiness |
| GET | `/api/v1/accounts` | List active accounts (`includeInactive=true` includes inactive rows) |
| GET | `/api/v1/accounts/{id}` | Read one account |
| GET | `/api/v1/categories` | List active categories |
| GET | `/api/v1/categories/{id}` | Read one category |
| GET | `/api/v1/transactions` | Filter, sort, and page transactions |
| GET | `/api/v1/transactions/{id}` | Read one transaction and return its ETag |
| POST | `/api/v1/transactions` | Create a transaction and return `201`, `Location`, and ETag |
| PUT | `/api/v1/transactions/{id}` | Update with required `If-Match` ETag |
| DELETE | `/api/v1/transactions/{id}` | Delete with required `If-Match` ETag |
| GET | `/api/v1/reports/activity` | Aggregate a full inclusive date range in PostgreSQL |
| GET | `/api/v1/events/receipts` | Show recently consumed Kafka event metadata |

Transactions accept `accountId`, `categoryId`, inclusive `from` and `to`, `search`, `sort` (`date`, `amount`, `payee`), `direction` (`asc`, `desc`), `page`, and `pageSize` (1–100). Sorting adds an ID tie-breaker. Search treats `%` and `_` as literal characters.

Dates use `YYYY-MM-DD`. Amounts remain `decimal`/JSON number/PostgreSQL `numeric`. Expenses are negative, income is positive, and zero amounts are invalid. API v1 does not support transfer writes.

ETags are strong quoted transaction versions such as `"1"`. Missing `If-Match` returns 428; a stale version returns 412 with code `transaction_version_conflict`. Validation responses use `ProblemDetails` with a stable `code` field.

Sample requests are in [FinanceTracker.Api/requests.http](../FinanceTracker.Api/requests.http). The event receipt endpoint returns IDs and timestamps, not event payloads.
