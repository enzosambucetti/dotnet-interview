# Synchronization Notes

## Architecture

The solution uses Hangfire as the execution engine and `SyncEvents` as the application source of truth for outbound synchronization.

Hangfire owns scheduling and execution:

- Enqueued jobs process outbound events.
- A recurring job polls inbound changes from `ExternalApi` every 1 minute.
- A recovery recurring job runs every 1 minute and re-enqueues retryable `SyncEvents`.

`SyncEvents` owns business audit/state:

- Entity and operation being synchronized.
- Payload snapshot.
- Processing status.
- Attempts and last error.
- Correlation ID.

Hangfire job state should not be treated as the source of truth for sync state.

## Why Hangfire

Hangfire was chosen over a plain `BackgroundService` because this sync flow benefits from durable jobs, recurring jobs, retry recovery, and an operational dashboard.

This is especially useful because inbound sync has no webhook/event source from `ExternalApi`, so the system polls every 1 minute.

Hangfire is also a pragmatic choice for this challenge: it keeps the solution self-contained, easy to run locally, and demonstrable without provisioning cloud infrastructure.

For a production cloud deployment, I would prefer an Azure-based architecture instead of running the synchronization workers inside the API process. A more production-oriented design would use:

- Azure Service Bus topics or queues for outbound sync events.
- Azure Functions or Durable Functions triggered asynchronously by Service Bus messages.
- Durable Functions orchestrations for multi-step sync workflows that need retries, compensation, or reconciliation.
- Timer-triggered Azure Functions for inbound polling when the external provider does not expose webhooks.
- Azure SQL as the application database and durable state store for local entities and sync event audit.
- Application Insights for distributed tracing, metrics, dashboards, and alerting.
- Azure Key Vault for connection strings, external API settings, and secrets.
- Managed Identity for Azure resource access.

In that version, the local API would persist the domain change and publish an outbound sync message. A Service Bus-triggered function would process the message, call the external API, update sync metadata, and move unrecoverable messages to a dead-letter flow for investigation.

Additional Azure production concerns that are intentionally out of scope for this challenge:

- Service Bus dead-letter queue monitoring and replay tooling.
- Idempotency keys or deduplication at the message and external API call level.
- Poison message handling and max delivery count policies.
- Durable orchestration state cleanup and retention policies.
- Distributed locks or single-consumer guarantees for per-entity ordering.
- Observability with correlation IDs across TodoApi, Service Bus, Functions, SQL, and ExternalApi.
- Alerting on failed sync rates, dead-letter growth, retry exhaustion, and inbound polling failures.
- Infrastructure-as-code for Azure resources, for example Bicep or Terraform.
- CI/CD deployment separation for API, Functions, database migrations, and infrastructure.

## Outbound Sync

Outbound is event-driven from local mutations:

- `TodoListCreated` -> `POST /todolists`
- `TodoListUpdated` -> `PATCH /todolists/{id}`
- `TodoListDeleted` -> `DELETE /todolists/{id}`
- `ItemCreated` -> `POST /todolists/{id}/todoitems`
- `ItemUpdated` -> `PATCH /todolists/{listId}/todoitems/{itemId}`
- `ItemDeleted` -> `DELETE /todolists/{listId}/todoitems/{itemId}`

The local service flow is:

1. Persist the local mutation.
2. Create a `SyncEvent`.
3. Enqueue a Hangfire outbound job with the `SyncEvent.Id`.

The `SyncEvent` is the durable record of the synchronization work. Hangfire is only the execution mechanism that picks up that work.

The outbound job:

- Reads the `SyncEvent`.
- Marks it `Processing`.
- Calls `ExternalApi`.
- Updates `ExternalId` metadata after creates.
- Marks the event `Completed` on success.
- Marks retryable failures as `FailedRetryable` and stores `LastError`.
- Marks deterministic non-retryable failures as `FailedTerminal`.
- Treats external `DELETE 404` as idempotent success.
- Treats external `CREATE`/`UPDATE 404` as a possible stale external reference and immediately runs inbound reconciliation.

## SyncEvents and Hangfire Atomicity

Local mutations persist the domain change and the `SyncEvent` before enqueueing the Hangfire job.

The Hangfire enqueue happens after `SaveChanges`, so it is not part of the same database transaction as the local mutation. This leaves a small failure window:

1. The local entity is saved.
2. The `SyncEvent` is saved as `Pending`.
3. The process crashes before the Hangfire job is enqueued.

If that happens, the change is not lost because the `SyncEvent` remains in the database. A recurring recovery job scans `Pending`, `FailedRetryable`, and legacy `Failed` events and re-enqueues them into Hangfire while `Attempts` is below the max attempt limit.

This provides eventual delivery without implementing a full transactional outbox. A production-grade version could replace this with a stricter outbox dispatcher if exactly-once enqueue semantics were required.

## Inbound Sync

Inbound is polling-based because `ExternalApi` has no webhook or events.

The recurring job runs every 1 minute and:

- Calls `GET /todolists`.
- Imports new external lists and items.
- Updates local lists/items when external values changed.
- Soft-deletes local lists/items that previously had `ExternalId` values and no longer appear externally.
- Does not generate outbound `SyncEvents`.

This prevents inbound-applied changes from echoing back outbound.

## Outbound Retry And Terminal Failures

Outbound sync distinguishes retryable and terminal failures through `SyncEvent.Status`:

- `Pending`: waiting to be processed or waiting on a local sync dependency.
- `Processing`: currently running.
- `Completed`: synchronized, canceled as obsolete, or resolved idempotently.
- `FailedRetryable`: failed with a transient or potentially recoverable condition.
- `FailedTerminal`: failed with a deterministic condition that should not be retried automatically.
- `Failed`: legacy retryable status kept for existing rows.

The recovery job only re-enqueues `Pending`, `FailedRetryable`, and legacy `Failed` rows with attempts below the configured limit. It does not re-enqueue `FailedTerminal`.

Examples of retryable failures:

- External `5xx`.
- Timeout or `HttpRequestException`.
- External `429` or request timeout.
- Unresolved outbound `404` after inbound reconciliation.

Examples of terminal failures:

- Invalid local state that cannot be repaired by ordering, such as an update without `ExternalId` and without a pending create event.
- External `4xx` contract failures other than the special cases handled by reconciliation.
- Unsupported sync event type/entity combinations.

## Outbound Dependency And Coalescing Rules

Some outbound events depend on earlier events:

- Item create/update needs the parent `TodoList.ExternalId`.
- TodoList update without `ExternalId` can wait for a pending TodoList create.
- Item update without `ExternalId` can wait for a pending Item create.

When a dependency is missing but expected to be produced by another pending sync event, the event stays `Pending`, records `LastError`, and does not consume an attempt.

Obsolete events are completed instead of sent out:

- Create/update for a soft-deleted local TodoList.
- Create/update for a soft-deleted local Item.
- Create/update for an Item whose parent TodoList was soft-deleted or removed.

Deletes without `ExternalId` are completed as local success because there is no known external target to delete.

## Create Reconciliation By Source ID

Create operations can be ambiguous: the external create may succeed but the response can be lost because of a timeout or transport failure. Retrying the create blindly can duplicate external data.

For outbound create timeout, transport failure, external `5xx`, or external `409`, the job runs inbound reconciliation before deciding the event failed. Inbound correlates by:

- `ExternalId` when available.
- External `source_id` matching local `SourceId`.
- External `source_id` matching the local numeric `Id` as string.

If reconciliation links the local entity to an external record, the create `SyncEvent` is marked `Completed`. If not, the failure is classified as retryable or terminal according to the error type.

## Conflict Policy

The first implementation uses a conservative timestamp policy:

- If external data changed and local `UpdatedAt` is newer than external `updated_at`, the job logs a conflict and skips overwriting local data.
- Otherwise inbound applies the external change locally.

This avoids silent data loss but does not yet provide automatic conflict resolution.

Future improvement: add explicit per-entity sync checkpoint metadata such as `LastSyncedAt`/hashes for more precise “changed since last sync” detection.

## Deletes

Local deletes are soft deletes:

- `IsDeleted = true`
- `DeletedAt = UtcNow`
- `UpdatedAt = DeletedAt`

Outbound delete sends hard delete requests to `ExternalApi`.

Inbound missing external records are interpreted as remote hard deletes and represented locally as soft deletes.

When outbound create/update receives `404`, the job does not blindly retry as if it were transient. It runs inbound reconciliation immediately. If inbound confirms that the target local entity, or its parent list for item events, was deleted externally and is now soft-deleted locally, the outbound `SyncEvent` is marked `Completed`. If reconciliation does not confirm deletion, the event is marked `FailedRetryable` and remains eligible for recovery retries until the configured attempt limit.

## Item Creation Assumption

The original challenge external API does not expose standalone item creation. It only creates items as part of `POST /todolists`.

For this local test environment, `ExternalApi` includes a pragmatic extension:

```http
POST /todolists/{todolistId}/todoitems
```

This endpoint exists so outbound `ItemCreated` can be exercised end to end. In a real external contract, this would need to be added officially or replaced with another agreed strategy.

This is an intentional extension over the original OpenAPI document, not an assumption that the published external contract already supports it. Without this endpoint, 
creating an item in an already-synced list would require a less direct workaround, such as provider-side contract changes or resubmitting/rebuilding list state, both of which have worse correctness and performance characteristics.

## Running

Start fake external API:

```powershell
dotnet run --project ExternalApi --launch-profile http
```

Start SQL Server in Docker:

```powershell
docker compose -f .devcontainer/docker-compose.yml up -d sqlserver
```

Apply migrations:

```powershell
dotnet ef database update --project TodoApi --startup-project TodoApi
```

Run local API:

```powershell
dotnet run --project TodoApi --launch-profile TodoApi
```

Hangfire dashboard is available in Development:

```http
/hangfire
```

## Postman Manual Validation

Manual API and sync flows are available in:

```text
TodoApi/PostmanCollections/TodoApi.Sync.postman_collection.json
```

The collection is named `TodoApi Sync Manual Tests` and includes:

- `TodoApi - Base Cases` for local API CRUD.
- `ExternalApi - Base Cases` for fake external API calls.
- `Sync Flow - Manual E2E` for outbound and inbound synchronization checks.

The manual sync flow is useful for exploratory validation around Hangfire and real HTTP calls between the two running APIs. Automated E2E tests remain deterministic by invoking sync jobs directly instead of waiting for scheduled Hangfire execution.

## Tests

Run:

```powershell
dotnet build
dotnet test
```

Covered scenarios include:

- Local mutations create `SyncEvents`.
- Outbound create updates local `ExternalId`.
- Outbound retryable/terminal failures increment `Attempts`, set `LastError`, and use `FailedRetryable` or `FailedTerminal`.
- External delete `404` completes successfully.
- Outbound create/update `404` runs inbound reconciliation before deciding whether to complete or retry.
- Item events wait for parent `ExternalId`.
- Obsolete create/update events over soft-deleted local records complete without external calls.
- Ambiguous create failures reconcile by `source_id`.
- Inbound imports external data.
- Inbound updates local data.
- Inbound soft-deletes missing external data.
- Inbound does not generate outbound events.

## E2E In-Process Tests

The E2E sync tests use `WebApplicationFactory` to run `TodoApi` and `ExternalApi` in process.

They intentionally do not wait for Hangfire recurring/enqueued execution because that would make the tests timing-dependent. Instead, each test:

1. Calls the local HTTP API.
2. Reads the created `SyncEvent`.
3. Executes `IOutboundSyncJob.ProcessAsync(syncEventId)` or `IInboundSyncJob.ProcessAsync(...)` directly.
4. Verifies the other API over HTTP.

This does not test Hangfire scheduling itself. It tests the deterministic business flow, persistence, HTTP client, and API-to-API contract. Hangfire is covered by configuration and smaller sync job tests.

Current E2E cases:

- `POST /api/todolists` in `TodoApi` creates the list in `ExternalApi` after outbound job execution.
- `POST /api/todolists/{id}/items` in `TodoApi` creates the item in `ExternalApi` after outbound job execution.
- Creating a list directly in `ExternalApi` imports it into `TodoApi` after inbound job execution.

## Local Detail Reads

`GET /api/todolists/{id}` returns the selected local list with its active `items` embedded. This mirrors the external read shape for detail inspection while keeping `GET /api/todolists` as a lightweight list endpoint without embedded items.

This is an intentional challenge/demo convenience. It makes it easier to show synchronization flows and validate imported or outbound-created items from a single local API request. For a production API, embedding child collections in the list detail endpoint should be evaluated more carefully because it can hide query cost, complicate pagination/filtering, and couple list reads to item payload shape. A production version would usually keep item reads explicit, or expose expansion/pagination options instead of always embedding all items.
