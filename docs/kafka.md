# Kafka event flow

Kafka demonstrates a real asynchronous boundary without splitting FinanceTracker into multiple deployable services.

## Event contract

The topic is `finance.transaction-events.v1`. Each JSON message includes:

- `eventId`: unique UUID, also used as the Kafka key;
- `eventType`: `transaction.created`, `transaction.updated`, or `transaction.deleted`;
- `occurredAtUtc`;
- `aggregateType` and `aggregateId`;
- `transaction`: the transaction snapshot at the time of the change.

The transaction snapshot is stored in the PostgreSQL outbox and Kafka message. The API receipt endpoint deliberately returns only event metadata.

## Delivery and recovery

Transaction changes and their outbox records are committed in one PostgreSQL transaction. The publisher retries unsent messages. Kafka acknowledgment happens before marking the outbox row published, so a publisher crash in between can result in a duplicate. This is at-least-once delivery.

The consumer disables automatic offset commits. It stores a receipt with the event UUID as a primary key, then commits the offset. If the same event is delivered again, the existing receipt makes the operation a no-op. If saving fails, the consumer leaves the offset uncommitted so Kafka can replay the message.

The broker is a single local KRaft node with no replication or authentication. This setup demonstrates the application patterns, not a production Kafka deployment. For a hosted system, add broker security, explicit topic lifecycle/retention, a dead-letter strategy, lag monitoring, outbox cleanup, and multi-node infrastructure.

## Demo

1. Start the stack with `./scripts/backend.ps1 up`.
2. Create a transaction through Swagger or `FinanceTracker.Api/requests.http`.
3. Read `/api/v1/events/receipts` until a `transaction.created` receipt appears.
4. Stop and restart Kafka while the API remains up, create another transaction, and observe that its outbox message is retried when Kafka returns.

The Kafka event flow is local-only and uses synthetic data. Kafka is not required to query or save a finance transaction; PostgreSQL remains authoritative.
