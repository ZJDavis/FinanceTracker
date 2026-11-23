# PostgreSQL performance investigation

This note is a reproducible measurement checklist. No query-plan result is recorded yet; capture it after the backend and local Compose stack have been exercised.

Use a synthetic dataset with at least 100,000 rows. Record the exact PostgreSQL image/version, machine, dataset row count, and commands. Do not use personal finance data.

Investigate these shapes with `EXPLAIN (ANALYZE, BUFFERS)`:

```sql
SELECT *
FROM transactions
WHERE "AccountId" = 1 AND "Date" >= DATE '2026-01-01' AND "Date" <= DATE '2026-12-31'
ORDER BY "Date" DESC, "Id" DESC
LIMIT 25;
```

```sql
SELECT SUM("Amount"), COUNT(*)
FROM transactions
WHERE "AccountId" = 1 AND "Date" >= DATE '2026-01-01' AND "Date" <= DATE '2026-12-31';
```

Record the plans, execution time, shared buffer reads/hits, and whether the account/date index is used. Test substring search separately before considering `pg_trgm`; do not add an index without measurement.
