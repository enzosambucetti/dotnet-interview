# Synchronization Notes

## Purpose

This document captures the main design decisions behind the synchronization implementation: what technology was used, why it was chosen, what trade-offs were accepted, and what would change in a production cloud architecture.

Operational details such as commands, ports, endpoint catalogs, Postman usage, and local setup instructions live in `AGENTS.md`.

## Architecture Decision

The solution treats `TodoApi` as the local system of record and `ExternalApi` as an external provider that must eventually stay in sync.

The implementation uses two separate concepts:

- `SyncEvents` hold business sync state.
- Hangfire executes and schedules work.

This separation is intentional. Hangfire job state is useful operationally, but it should not be the source of truth for domain synchronization. The durable sync audit lives in the application database through `SyncEvents`, including entity type, operation, payload snapshot, status, attempts, last error, and correlation id.


## Why Hangfire

Hangfire was chosen for this challenge because it provides durable background jobs, recurring jobs, retry visibility, and a local dashboard without requiring cloud infrastructure.

It is a pragmatic middle ground between a plain `BackgroundService` and a full cloud/event-driven architecture:

- Better than a simple in-memory channel for this case because work survives process restarts.
- Better than a raw timer because recurring jobs and operational status are visible.
- Simpler than Azure Functions plus Service Bus for a local interview challenge.

The trade-off is that synchronization workers run inside the API process. That is acceptable for a self-contained challenge, but it is not the preferred production architecture.

## Visual Documentation

The repository root includes Mermaid-based HTML documentation in Spanish for practical challenge walkthroughs:

- `sync-architecture.html`: synchronization architecture, outbound/inbound flows, error handling, retries, reconciliation, and operational view.
- `signalr-architecture.html`: local self-hosted SignalR architecture, event payloads, local API events, inbound sync events, and frontend connection expectations.

These files are intended for demos, review, and explaining the system flow during the challenge. They are not generated build artifacts and should be kept aligned with `NOTES.md` and `AGENTS.md` when sync or realtime behavior changes.

## Production Direction

For production, I would move synchronization execution out of the API process and into Azure-managed workers.

The proposed cloud architecture would use:

- Azure Service Bus topics or queues for outbound sync events.
- Azure Functions or Durable Functions for asynchronous sync processing.
- Timer-triggered Azure Functions for inbound polling while the external provider has no webhook/event contract.
- Azure SQL for domain data and sync audit state.
- Application Insights for traces, metrics, dashboards, and alerts.
- Key Vault and Managed Identity for secrets and Azure resource access.

In that model, `TodoApi` would persist the local mutation in Azure SQL and publish an outbound sync message to Service Bus. A Service Bus-triggered Function would consume the message, call the external API, and update the sync status in Azure SQL. Retryable failures would be retried by the Function/Service Bus pipeline, while exhausted or invalid messages would move to a dead-letter queue for investigation and replay.

Inbound sync would be handled by a Timer-triggered Function. It would periodically read the external API, compare the external state with Azure SQL, and apply inserts, updates, or local soft deletes. If inbound changes need to notify clients, the Function could publish an internal event or call a notification service after the database update.

Durable Functions would be useful if the sync flow grows into a multi-step workflow that needs explicit orchestration, waiting, compensation, or reconciliation across several external calls. For the current challenge scope, a normal Service Bus-triggered Function plus a Timer-triggered Function would be enough.

Additional production concerns would include dead-letter replay tooling, idempotency keys, poison message policies, per-entity ordering, distributed tracing, infrastructure-as-code, and CI/CD separation for API, workers, migrations, and infrastructure.

## High-Level Flow

Outbound sync is event-driven from local mutations:

1. `TodoApi` persists the local change.
2. `TodoApi` creates a `SyncEvent`.
3. Hangfire enqueues an outbound job for that `SyncEvent`.
4. The outbound job calls `ExternalApi`.
5. The job updates local sync metadata, such as `ExternalId`, and completes or fails the `SyncEvent`.

Inbound sync is polling-based because the external provider has no webhook or event source:

1. A recurring Hangfire job calls `ExternalApi`.
2. The job imports new external lists/items.
3. The job updates local data when external data changed.
4. The job soft-deletes local records that previously had an `ExternalId` but no longer exist externally.
5. Inbound-applied changes do not create outbound `SyncEvents`, preventing echo loops.

Realtime UI updates are handled separately through self-hosted SignalR. Sync jobs and local API mutations publish events so connected clients can refresh from the REST API.

## Atomicity And Recovery

The local domain mutation and the `SyncEvent` are saved before the Hangfire job is enqueued. The enqueue is not part of the exact same database transaction, so there is a small failure window where the local change and `SyncEvent` exist but the Hangfire job was not created.

That risk is mitigated by the recovery recurring job. It scans pending or retryable `SyncEvents` and re-enqueues them while they are still below the attempt limit.

This is an eventual-delivery approach, not a strict transactional outbox. A production-grade version could use a formal outbox dispatcher if exactly-once enqueue semantics were required.

## Manual Sync Repair

`SyncRepairJob` exists for operator-driven recovery only. It is registered in Hangfire as `todoapi-sync-repair-manual` with a never-running schedule, so it is visible from the dashboard and can be executed with `Trigger now`, but it does not run automatically.

The job repairs two practical failure cases:

- Retryable outbound events that reached the max attempt limit are reset to `Pending`, with `Attempts=0` and `LastError` cleared, then enqueued again.
- Active local `TodoList` or `Item` rows without `ExternalId` and without an existing create `SyncEvent` get a new `Created` event and are enqueued.

Before creating missing outbound events, the job runs inbound reconciliation best effort. This reduces duplicate creates by giving the system a chance to link local rows to existing external records by `source_id`.

The job intentionally does not reopen `FailedTerminal`, `Completed`, or `Processing` events. Terminal failures should be inspected and corrected explicitly before replay.

## Retry Policy

Outbound sync owns retry classification through `SyncEvent.Status`:

- `Pending`: waiting to run or waiting for a dependency.
- `Processing`: currently running.
- `Completed`: synchronized, obsolete, or resolved idempotently.
- `FailedRetryable`: recoverable failure eligible for recovery.
- `FailedTerminal`: deterministic failure that should not retry automatically.
- `Failed`: legacy retryable status kept for compatibility.

The recovery job only re-enqueues pending/retryable states.

Inbound sync intentionally disables Hangfire automatic retries. Since inbound already polls every minute, failed inbound attempts should fail once and retry naturally on the next polling tick. This avoids building up scheduled retry jobs when `ExternalApi` is temporarily unavailable. Inbound also disables concurrent execution so slow polls do not overlap.

## Dependencies And Ordering

Some outbound events require data created by earlier sync events:

- Item create/update needs the parent list `ExternalId`.
- List update without `ExternalId` can wait for a pending list create.
- Item update without `ExternalId` can wait for a pending item create.

When a missing dependency is expected to be produced by another pending event, the event stays `Pending` and does not consume an attempt.

Obsolete create/update events are completed without external calls when the local entity, item, or parent list was already soft-deleted.

## Reconciliation

Create operations are ambiguous when the external call succeeds but the response is lost. Retrying blindly can duplicate external records.

For timeout, transport failure, external `5xx`, or external `409` during create, the outbound job runs inbound reconciliation before deciding whether to retry. Reconciliation matches by:

- Existing local `ExternalId`.
- External `source_id` matching local `SourceId`.
- External `source_id` matching the local numeric id as a string.

If reconciliation links the local entity to an external record, the sync event is completed. Otherwise, the original failure is classified as retryable or terminal.

Outbound create/update `404` is also reconciled. If inbound confirms that the external record disappeared and the local record is now soft-deleted, the outbound event completes instead of retrying to exhaustion.

## Conflict Policy

The current conflict policy is intentionally conservative:

- If external data changed but local `UpdatedAt` is newer than external `updated_at`, inbound logs the conflict and does not overwrite local data.
- Otherwise inbound applies the external change locally.

This avoids silent data loss, but it is not a full conflict-resolution system.

Future improvement: add explicit per-entity sync checkpoints, such as `LastSyncedAt`, version hashes, or provider revision ids, to distinguish local-only changes from already-synchronized state more precisely.

## Delete Policy

Local deletes are soft deletes so local history and reconciliation remain possible.

Outbound deletes are sent as hard deletes to `ExternalApi`, because the external contract does not expose soft delete semantics.

Inbound treats missing external records as remote hard deletes and represents that locally as soft deletes.

If inbound sees an external record whose `ExternalId` matches a local soft-deleted entity, it restores the local entity only when `DeletedAt < external.created_at`. That case usually means the fake in-memory `ExternalApi` reused an id after reset/reseed and the external record is newer than the local tombstone. Restore clears `IsDeleted`/`DeletedAt` and refreshes the local fields from the external payload.

When `DeletedAt >= external.created_at`, inbound leaves the local entity soft-deleted and does not import child items under it. This can be a normal race where a local delete has happened but the outbound delete has not reached `ExternalApi` yet, so it is not logged as a conflict.

This means normal local API reads hide deleted records, while sync logic can still inspect them with query filters disabled.

`ExternalApi` keeps deterministic ids and deterministic seed timestamps for repeatable demos, but runtime creates/updates use `DateTimeOffset.UtcNow`. This lets inbound compare external runtime records against local `DeletedAt` values without treating new Postman-created data as if it came from the January 2026 seed.

## HTTP Errors And Logging

The API uses RFC 7807-style `ProblemDetails` for HTTP errors. This keeps client-facing errors predictable and avoids leaking raw exception messages. Controlled errors include stable error codes and trace ids, while unexpected exceptions include an error id that can be used to find the matching server log.

Background sync failures are handled differently. They are not returned to an HTTP client, so their durable state belongs in `SyncEvents`: status, attempts, `LastError`, and correlation id. Logs provide operational context, while `SyncEvents` remain the business audit trail.

Current logging is intentionally simple for the challenge: console logging with warning/error entries for HTTP errors, external API retries, sync reconciliation, terminal/retryable failures, and inbound conflicts. In production, this should move to centralized observability such as Application Insights/OpenTelemetry with dashboards and alerts for failed sync rates, retry exhaustion, and dead-letter/replay flows.

## External Contract Assumption

The original challenge external API creates items only as part of `POST /todolists`; it does not expose standalone item creation for an existing list.

For this local test environment, `ExternalApi` includes one intentional extension:

```http
POST /todolists/{todolistId}/todoitems
```

This endpoint exists so outbound `ItemCreated` can be exercised end to end. It is not treated as part of the original published external contract.

Without that extension, creating an item in an already-synced list would require either a provider contract change or a less direct workaround such as rebuilding list state, which has worse correctness and performance characteristics.

## Testing Strategy

The automated tests focus on deterministic behavior rather than waiting for real Hangfire timers.

The E2E tests host `TodoApi` and `ExternalApi` in process, call the local HTTP API, execute the sync job directly, and verify the opposite API over HTTP. This validates the business flow, persistence, HTTP client, and API-to-API contract without timing-dependent sleeps.

Hangfire itself is treated as execution infrastructure. Its configuration and job scheduling behavior are covered separately from the core sync business rules.

Manual validation through Postman remains useful for demonstrating the full local runtime with SQL Server, Hangfire dashboard, both APIs, and real HTTP calls.

The Postman collection lives under `TodoApi/PostmanCollections` and covers the manual validation scenarios.

## Dev Container Support

The solution is intended to run through dev containers even if development was done locally. The backend dev container owns the .NET runtime and SQL Server dependency; `TodoApi` and `ExternalApi` are run from terminals inside that container. The frontend dev container owns the Node/Vite runtime and reaches the backend through `host.docker.internal:5083`, with Vite proxying both REST (`/api`) and SignalR (`/hubs`) traffic.

Detailed dev container startup steps live in each repository's `AGENTS.md`.

## AI Assistance

Relevant AI conversations were saved in the root `AIConversations` folder to keep implementation context, design discussion, and decision history available for review.

Codex CLI was used as the primary coding agent, selecting GPT models according to the workload and task complexity. Windsurf was also used as an agentic AI IDE to vary model behavior when useful, including Claude Opus for workloads that benefited from a different model profile.

On the frontend, Vercel React/design skills were used to guide React implementation and UI decisions toward established best practices. The referenced skill source is `skills.sh`.

## Future Improvements

- **Transactional outbox**: replace the current `SyncEvent` + recovery enqueue mitigation with a stricter outbox dispatcher if exactly-once enqueue semantics become required.
- **Cloud-native sync workers**: move sync execution out of the API process into Azure Service Bus-triggered Functions and Timer-triggered Functions for better scaling, isolation, and operations.
- **Operational tooling**: add admin views or scripts for inspecting, replaying, canceling, or force-completing `SyncEvents`.
- **Observability**: move from console-only logs to Application Insights/OpenTelemetry with dashboards and alerts for failed sync rates, retry exhaustion, inbound polling failures, and external API latency.
- **Performance**: add pagination/filtering for large TodoLists and avoid full-list inbound comparisons if the external provider later supports incremental changes or updated-since queries.
- **Security/configuration**: move secrets and environment-specific values out of checked-in config for production, using Key Vault/Managed Identity or equivalent secret management.
- **More E2E coverage**: expand automated E2E tests for delete flows, conflict scenarios, recovery re-enqueue behavior, and SignalR notification publishing.

## Challenge Coverage Checklist

- Local Todo API persists TodoLists and Items.
- Fake `ExternalApi` is included for local development with deterministic seed data and generated IDs.
- External API base contract matches the challenge documentation for list create/update/delete, item update/delete, and list reads with embedded items.
- Local-only `ExternalApi` item-create extension is documented as intentional.
- Outbound synchronization covers local create/update/delete of TodoLists and Items.
- Inbound synchronization covers external create/update/delete detection through polling.
- `SyncEvents` provide durable business sync state.
- Hangfire provides local execution, recurring jobs, recovery, and dashboard visibility.
- Recovery mitigates the non-transactional enqueue window after local persistence.
- Inbound polling avoids automatic retry buildup and overlapping polls.
- Local deletes are soft deletes; external deletes are hard deletes.
- Missing external records are reconciled locally as soft deletes.
- Outbound `404` handling performs inbound reconciliation before retrying.
- Ambiguous outbound creates reconcile by `source_id` to reduce duplicate external records.
- Conflict behavior is documented, including the current limitations.
- HTTP errors use `ProblemDetails`; sync errors use `SyncEvents` plus logs.
- Automated tests cover service behavior, sync jobs, and deterministic in-process E2E sync scenarios.
- Postman collection exists for manual runtime validation.
- Backend and frontend dev container startup paths are documented.
- Production Azure direction and out-of-scope operational concerns are documented.
