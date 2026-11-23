# SQL query safety

All transaction INSERT, UPDATE, and DELETE operations use fixed SQL statements
with named, explicitly typed parameters. The private `Bind` method in
`FinanceTracker.Data/SqliteFinanceStore.cs` binds field values without inserting
them into SQL text. IDs, versions, dates, account/category IDs, payees, memos, and
amounts are parameterized. Reads and schema migrations use constant SQL. Search
currently runs in memory and does not create SQL queries.

Payee and Memo may contain quotes, semicolons, comments, and SQL keywords. Those
characters are stored as literal text. Keyword removal or quote stripping is not
the protection: parameter binding keeps input separate from executable SQL.
Dates, identifiers, and amounts are also represented by typed C# properties.

When adding account/category editing, CSV import, database search, or other queries,
continue using fixed SQL and parameters. Parameters cannot represent table/column
names; any future selectable identifiers must come from a fixed allowlist in code.
Never concatenate or interpolate user field values into `CommandText`.

## Tests and evidence

`Tests/Unit/SqliteEdgeCaseTests.cs` contains six injection-payload cases covering
insert and update of both Payee and Memo. Tests assert exact text round trips,
unchanged sentinel rows and money, intact lookup tables, and targeted deletion.
Each case runs against a real, isolated temporary SQLite database.

On October 7, 2026, all six cases passed with typed parameters. In an isolated
copy, replacing Payee binding with raw quote concatenation made five of those
six cases fail. The payload without quote-breaking text still passed, as expected.
The temporary unsafe copy was removed; the working implementation was unchanged
during that experiment. This demonstrates that the tests detect the unsafe change.

The complete verification passed: 43 xUnit cases, 17 regression checks, and a build
with zero warnings/errors. Coverage includes locks and retry, read-only/corrupt
databases, schema migration, stale updates/deletes, monetary and date boundaries,
Unicode/long text, and persistence across separate processes.

These results apply to the current query paths. They do not claim to test every
possible future query or provide database encryption/access control. Full UI
interaction and arbitrary operating-system permission combinations remain outside
the automated suite.

## API and event-flow boundary

The API uses EF Core LINQ for PostgreSQL queries, which sends user values as
database parameters. Transaction search escapes `%`, `_`, and the escape character
so they remain literal substring characters. The Compose stack publishes only
the API on `127.0.0.1`; PostgreSQL and Kafka are private to the Compose network.
The default API has no authentication and must not be exposed to other machines.

Kafka messages contain synthetic transaction snapshots in the local demo. The
receipt API returns event IDs and timestamps only. PostgreSQL remains the source
of truth; the outbox provides at-least-once delivery and consumer idempotency, not
exactly-once processing or broker high availability.

The new PostgreSQL/API integration suite has been added but has not yet been run.
Its coverage must be confirmed against a working Docker engine before treating
these backend security and concurrency behaviors as verified.
