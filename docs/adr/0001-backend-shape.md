# ADR 0001: Backend shape and data modes

**Status:** Accepted for the portfolio demo

## Context

FinanceTracker is a Windows WPF application with an existing SQLite data store. The backend should add service and database experience while remaining understandable and runnable on a developer workstation.

## Decisions

- Keep SQLite as the desktop app's default and preserve its existing database file.
- Add an explicit, opt-in HTTP API data mode. Switching modes never transfers data.
- Build one ASP.NET Core API modular monolith with Application and PostgreSQL persistence projects.
- Use PostgreSQL migrations as the API database schema source of truth.
- Add a local Kafka workflow for transaction change events using a PostgreSQL transactional outbox and idempotent receipt consumer. Run the publisher and consumer in the API process.
- Keep the API, database, and broker local-only by default. No authentication or public deployment is part of this demo.

## Consequences

- Developers can review REST, EF Core, PostgreSQL, ETag concurrency, Docker Compose, Kafka, outbox, and idempotent consumer patterns in one repository.
- The Compose stack is heavier than PostgreSQL alone. Kafka is still optional at runtime through `Kafka:Enabled=false`, and finance writes remain available during a broker outage.
- The single-node broker has no high availability. The outbox may deliver duplicates, and consumers must remain idempotent.
- Any hosted/shared deployment needs a new security and operations design before exposure.
