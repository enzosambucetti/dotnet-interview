# AGENTS.md

## Project Overview

This repository contains a .NET 8 solution for a Todo API challenge plus a local fake external API used to develop and test synchronization logic.

Projects:

- `TodoApi`: main local API. Persists data with EF Core and SQL Server.
- `TodoApi.Tests`: xUnit tests for the local API controllers.
- `ExternalApi`: in-memory fake implementation of the external Todo API contract from `crunchloop/challenge-senior-engineer`.

Solution:

- `dotnet-interview.sln`

Use repo conventions already present in the codebase. Do not introduce unrelated rewrites or architecture churn.

## Running The APIs

### TodoApi With SQL Server In Docker

`TodoApi` reads `DatabaseTarget` from configuration and uses it to select a connection string. The default in `TodoApi/appsettings.json` is:

```json
"DatabaseTarget": "DockerSql"
```

Docker SQL Server connection:

```json
"DockerSql": "Server=localhost,1433;Database=Todos;User Id=sa;Password=Password123;TrustServerCertificate=True;"
```

Start SQL Server:

```powershell
docker compose -f .devcontainer/docker-compose.yml up -d sqlserver
```

Apply migrations:

```powershell
dotnet ef database update --project TodoApi --startup-project TodoApi
```

Run the API:

```powershell
dotnet run --project TodoApi --launch-profile TodoApi
```

Default launch URLs:

- `https://localhost:7027`
- `http://localhost:5083`

Swagger is enabled in Development at `/swagger`.

Hangfire dashboard is enabled in Development at:

- `http://localhost:5083/hangfire`
- `https://localhost:7027/hangfire`

### TodoApi With Local SQL Server Instance

Use the `LocalSql` connection string when running against a local SQL Server or SQL Express instance.

Configured default:

```json
"LocalSql": "Server=DESKTOP-LIRJ2TG\\SQLEXPRESS;Database=Todos;Trusted_Connection=True;TrustServerCertificate=True;"
```

Prefer overriding config via environment variables instead of hardcoding machine-specific values:

```powershell
$env:DatabaseTarget = "LocalSql"
$env:ConnectionStrings__LocalSql = "Server=YOUR_SERVER\\SQLEXPRESS;Database=Todos;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet ef database update --project TodoApi --startup-project TodoApi
dotnet run --project TodoApi --launch-profile TodoApi
```

If the checked-in `LocalSql` connection string matches the machine, only `DatabaseTarget=LocalSql` is needed.

After startup, Hangfire dashboard is available in Development at:

- `http://localhost:5083/hangfire`
- `https://localhost:7027/hangfire`

### Clearing Local TodoApi Database Data

Use `scripts/Clear-TodoApiDatabase.ps1` to delete application data from the configured SQL Server database without dropping the schema or EF migrations.

Default cleaned tables:

- `dbo.SyncEvents`
- `dbo.Items`
- `dbo.TodoList`

The script preserves:

- `__EFMigrationsHistory`
- table schema
- stored migrations
- Hangfire tables by default

Clean the default configured target from `TodoApi/appsettings.json`:

```powershell
.\scripts\Clear-TodoApiDatabase.ps1
```

Clean Docker SQL Server explicitly:

```powershell
.\scripts\Clear-TodoApiDatabase.ps1 -DatabaseTarget DockerSql
```

Clean local SQL Server explicitly:

```powershell
.\scripts\Clear-TodoApiDatabase.ps1 -DatabaseTarget LocalSql
```

Clean using an explicit connection string:

```powershell
.\scripts\Clear-TodoApiDatabase.ps1 -ConnectionString "Server=localhost,1433;Database=Todos;User Id=sa;Password=Password123;TrustServerCertificate=True;"
```

Optionally also clear Hangfire runtime tables:

```powershell
.\scripts\Clear-TodoApiDatabase.ps1 -DatabaseTarget DockerSql -IncludeHangfire
```

Use `-IncludeHangfire` only when no TodoApi/Hangfire server instance is actively processing jobs.

### ExternalApi

`ExternalApi` does not use a database. It stores in-memory data through a singleton store and resets on process restart. Seed rows and generated IDs are deterministic; runtime mutation timestamps use current UTC time.

Run:

```powershell
dotnet run --project ExternalApi --launch-profile http
```

Default launch URLs:

- `http://localhost:5090`
- `https://localhost:7090`

Swagger is enabled in Development at `/swagger`.

Development-only reset endpoint:

```http
POST /__test/reset
```

### Running With Dev Containers

The solution is fully functional using VS Code dev containers. The backend dev container includes the .NET SDK, SQL Server, and automatic migration setup.

Prerequisites: Docker Desktop running.

Steps:

1. Open `dotnet-interview` in VS Code.
2. Accept **"Reopen in Container"** or run `Dev Containers: Reopen in Container` from the command palette.
3. Wait for `postCreateCommand` to finish (restore, build, migrations).
4. Open two terminals inside the container.

Terminal 1 - TodoApi:

```bash
dotnet run --project TodoApi --launch-profile TodoApi
```

Terminal 2 - ExternalApi:

```bash
dotnet run --project ExternalApi --launch-profile http
```

Forwarded ports accessible from the host browser:

- TodoApi: `http://localhost:5083/swagger`
- ExternalApi: `http://localhost:5090/swagger`
- Hangfire: `http://localhost:5083/hangfire`
- SQL Server: `localhost:1433`

Inside the backend container, SQL Server is reached through the `sqlserver` hostname. This is configured by `ConnectionStrings__DockerSql` in `.devcontainer/docker-compose.yml`; `appsettings.json` remains useful for local development outside the container.

Hangfire and SignalR work without extra configuration:

- Hangfire uses the same SQL Server database and creates its tables on startup (`PrepareSchemaIfNecessary = true`). Recurring jobs register automatically.
- SignalR hub `/hubs/todo-updates` is available on port 5083. CORS allows `http://localhost:5173` by default.

To run the React frontend in its own dev container alongside this backend, see the frontend repository `AGENTS.md`. The frontend Vite proxy uses `VITE_API_TARGET=http://host.docker.internal:5083` inside its container to reach this backend through Docker host networking.

## Postman Collections

Manual API and sync validation is covered by:

- `TodoApi/PostmanCollections/TodoApi.Sync.postman_collection.json`

Collection name:

- `TodoApi Sync Manual Tests`

The collection contains three folders:

- `TodoApi - Base Cases`: CRUD coverage for the local `/api/todolists` and nested `/api/todolists/{id}/items` endpoints.
- `ExternalApi - Base Cases`: direct calls to the fake external API, including reset, list CRUD, and item CRUD through the local test extension.
- `Sync Flow - Manual E2E`: numbered manual flow for outbound and inbound synchronization checks between `TodoApi` and `ExternalApi`.

Collection variables:

- `todoApiBaseUrl`: default `http://localhost:5083`
- `externalApiBaseUrl`: default `http://localhost:5090`
- `todoListId`
- `itemId`
- `externalListId`
- `externalItemId`

Before running the sync flow manually:

1. Start SQL Server and apply migrations.
2. Start `ExternalApi`.
3. Start `TodoApi`.
4. Reset external data with `POST /__test/reset` or the `00 - Reset ExternalApi` request.

For inbound sync steps, either wait for the recurring Hangfire job, which runs every 1 minute, or trigger it manually from the dashboard:

```http
http://localhost:5083/hangfire
```

Use `Recurring Jobs -> todoapi-inbound-sync -> Trigger now`.

## Synchronized Models

There are two logical entities to synchronize between `TodoApi` and `ExternalApi`.

### Todo List

Local model: `TodoApi.Models.TodoList`

Properties:

- `Id: long`
- `SourceId: string?`, JSON `source_id`
- `ExternalId: string?`, JSON `external_id`
- `Name: string`
- `CreatedAt: DateTimeOffset`, JSON `created_at`
- `UpdatedAt: DateTimeOffset`, JSON `updated_at`
- `IsDeleted: bool`, JSON `is_deleted`
- `DeletedAt: DateTimeOffset?`, JSON `deleted_at`
- Detail response only: `Items: IList<Item>`, JSON `items`

External model: `ExternalApi.Models.TodoList`

Properties:

- `Id: string`, JSON `id`
- `SourceId: string?`, JSON `source_id`
- `Name: string?`, JSON `name`
- `CreatedAt: DateTimeOffset`, JSON `created_at`
- `UpdatedAt: DateTimeOffset`, JSON `updated_at`
- `Items: IList<TodoItem>`, JSON `items`

Mapping guidance:

- Local `TodoList.Id` is the local numeric identity.
- External `TodoList.id` is the external string identity.
- `source_id` is the cross-system correlation field.
- Local `GET /api/todolists/{id}` returns a detail DTO with embedded local `items`.
- Local `GET /api/todolists` does not embed items; external `GET /todolists` does.

### Todo Item

Local model: `TodoApi.Models.Item`

Properties:

- `Id: long`
- `SourceId: string?`, JSON `source_id`
- `ExternalId: string?`, JSON `external_id`
- `Name: string`
- `IsCompleted: bool`
- `CreatedAt: DateTimeOffset`, JSON `created_at`
- `UpdatedAt: DateTimeOffset`, JSON `updated_at`
- `IsDeleted: bool`, JSON `is_deleted`
- `DeletedAt: DateTimeOffset?`, JSON `deleted_at`
- `TodoListId: long`
- `TodoList: TodoList`, JSON ignored

External model: `ExternalApi.Models.TodoItem`

Properties:

- `Id: string`, JSON `id`
- `SourceId: string?`, JSON `source_id`
- `Description: string?`, JSON `description`
- `Completed: bool`, JSON `completed`
- `CreatedAt: DateTimeOffset`, JSON `created_at`
- `UpdatedAt: DateTimeOffset`, JSON `updated_at`

Mapping guidance:

- Local `Item.Name` maps to external `TodoItem.description`.
- Local `Item.IsCompleted` maps to external `TodoItem.completed`.
- Local `Item.TodoListId` is represented externally by nesting and route path, not by a field on `TodoItem`.
- `source_id` should be used to correlate item identity across APIs.

## TodoApi Contracts

Base route prefix: `/api`

Todo lists controller: `TodoApi.Controllers.TodoListsController`

- `GET /api/todolists`
- `GET /api/todolists/{id}`
- `POST /api/todolists`
- `PUT /api/todolists/{id}`
- `DELETE /api/todolists/{id}`

Todo list DTOs:

- `CreateTodoList`: `source_id`, `name`, `created_at`, `updated_at`
- `UpdateTodoList`: `source_id`, `name`, `updated_at`
- `TodoListDetail`: list fields plus `items`

Items controller: `TodoApi.Controllers.ItemsController`

- `GET /api/todolists/{todoListId}/items`
- `GET /api/todolists/{todoListId}/items/{id}`
- `POST /api/todolists/{todoListId}/items`
- `PUT /api/todolists/{todoListId}/items/{id}`
- `DELETE /api/todolists/{todoListId}/items/{id}`

Item DTOs:

- `CreateItem`: `source_id`, `name`, `isCompleted`, `created_at`, `updated_at`
- `UpdateItem`: `source_id`, `name`, `isCompleted`, `updated_at`

Behavior:

- Create endpoints preserve incoming sync timestamps when supplied.
- Create endpoints set `CreatedAt` and `UpdatedAt` to `DateTimeOffset.UtcNow` when omitted.
- Update endpoints update `UpdatedAt` to the incoming value when supplied, otherwise `DateTimeOffset.UtcNow`.
- `SourceId` is updated only when a non-null value is supplied.
- Delete endpoints are soft deletes in `TodoApi`.
- Soft-deleted entities set `IsDeleted=true`, `DeletedAt=UtcNow`, and `UpdatedAt=DeletedAt`.
- Normal EF queries use query filters to exclude soft-deleted `TodoList` and `Item` rows.
- Soft-deleting a local `TodoList` also soft-deletes its active local `Item` rows.

## ExternalApi Contracts

External route prefix: none.

Todo lists controller: `ExternalApi.Controllers.TodoListsController`

- `GET /todolists`
- `POST /todolists`
- `PATCH /todolists/{todolistId}`
- `DELETE /todolists/{todolistId}`

Todo list contracts:

- `CreateTodoListBody`: `source_id`, `name`, `items`
- `UpdateTodoListBody`: `name`

Todo items controller: `ExternalApi.Controllers.TodoItemsController`

- `POST /todolists/{todolistId}/todoitems`
- `PATCH /todolists/{todolistId}/todoitems/{todoitemId}`
- `DELETE /todolists/{todolistId}/todoitems/{todoitemId}`

Todo item contracts:

- `CreateTodoItemBody`: `source_id`, `description`, `completed`
- `UpdateTodoItemBody`: `description`, `completed`

External API behavior:

- `GET /todolists` returns lists with embedded items.
- `POST /todolists` creates a list and optional items in one request.
- `POST /todolists/{todolistId}/todoitems` is a local fake API extension for creating a standalone item under an existing list. It exists to exercise outbound item sync in the challenge implementation.
- External IDs are deterministic strings like `ext-list-001` and `ext-item-001`.
- Seed timestamps are deterministic. Runtime create/update timestamps use `DateTimeOffset.UtcNow` so manual Postman data can be compared correctly against local soft-delete tombstones.

### ExternalApi Seed Data

`ExternalApi` stores data in memory. On process startup and on `POST /__test/reset`, `ExternalTodoStore.Reset()` restores this exact seed:

```json
[
  {
    "id": "ext-list-001",
    "source_id": "local-list-1",
    "name": "External Work",
    "created_at": "2026-01-01T00:00:00+00:00",
    "updated_at": "2026-01-01T00:00:00+00:00",
    "items": [
      {
        "id": "ext-item-001",
        "source_id": "local-item-1",
        "description": "Review sync design",
        "completed": false,
        "created_at": "2026-01-01T00:00:00+00:00",
        "updated_at": "2026-01-01T00:00:00+00:00"
      },
      {
        "id": "ext-item-002",
        "source_id": "local-item-2",
        "description": "Prepare demo data",
        "completed": true,
        "created_at": "2026-01-01T00:00:00+00:00",
        "updated_at": "2026-01-01T00:00:00+00:00"
      }
    ]
  },
  {
    "id": "ext-list-002",
    "source_id": "local-list-2",
    "name": "External Personal",
    "created_at": "2026-01-01T00:00:00+00:00",
    "updated_at": "2026-01-01T00:00:00+00:00",
    "items": [
      {
        "id": "ext-item-003",
        "source_id": "local-item-3",
        "description": "Buy coffee",
        "completed": false,
        "created_at": "2026-01-01T00:00:00+00:00",
        "updated_at": "2026-01-01T00:00:00+00:00"
      }
    ]
  }
]
```

After reset, deterministic counters are:

- Next list id: `ext-list-003`.
- Next item id: `ext-item-004`.

Runtime timestamps after reset:

- Creates and updates performed after startup/reset use `DateTimeOffset.UtcNow`.
- The fixed `2026-01-01T00:00:00+00:00` timestamp is only for seed records.

## Persistence And Migrations

`TodoApi` uses EF Core SQL Server through `TodoContext`.

Current local tables:

- `TodoList`
- `Items`

Important migrations:

- `20241126134649_CreateTodoLists`
- `20260428183225_CreateItems`
- `20260505013854_AddSynchronizationProperties`
- `20260505020118_AddSoftDeleteProperties`
- `20260505023943_AddSyncEventsAndExternalIds`

Sync tables:

- `SyncEvents`: source of truth/audit for outbound synchronization.

Hangfire tables are execution infrastructure only. Do not use Hangfire job state as domain sync state.

Sync event statuses:

- `Pending`: waiting to run, or waiting on another local sync event to provide a dependency.
- `Processing`: currently running.
- `Completed`: done, idempotently resolved, or canceled because the event became obsolete.
- `FailedRetryable`: failed with a transient or recoverable condition and can be re-enqueued by recovery.
- `FailedTerminal`: failed with a deterministic non-retryable condition.
- `Failed`: legacy retryable status; keep recovery compatibility for existing rows.

When adding persisted properties to synchronized entities:

- Update the model.
- Update create/update DTOs if the property is part of the API contract.
- Update controller/service write paths.
- Add an EF migration.
- Update tests.

## Error Handling

There are two error-handling contexts.

### HTTP API Errors

HTTP request/response errors should use RFC 7807-style `ProblemDetails`.

Current behavior:

- Controlled not-found cases return `application/problem+json` with HTTP 404.
- Unhandled HTTP exceptions are converted by `UnhandledExceptionMiddleware` into HTTP 500 `ProblemDetails`.
- ProblemDetails responses include tracking fields:
  - `traceId`: ASP.NET Core request trace identifier.
  - `errorCode`: stable application error code for controlled errors.
  - `errorId`: generated identifier for unhandled exceptions.
  - `eventId`: logger event id for unhandled exceptions.

Logging:

- Current provider setup is console-only in `TodoApi/Program.cs` through `builder.Logging.ClearProviders()` and `builder.Logging.AddConsole()`.
- Default log levels are configured in `TodoApi/appsettings.json` and `TodoApi/appsettings.Development.json`.
- Use `ApiErrorEventIds` for stable log event IDs.
- Log controlled HTTP errors at warning level.
- Log unhandled HTTP exceptions at error level.
- Include relevant route IDs and `traceId` in log messages.
- `ExternalTodoApiClient` logs failed outbound HTTP attempts before retrying.
- Sync jobs log reconciliation, obsolete events, dependency waits, conflicts, and retryable/terminal failures.
- Keep production-oriented observability centralized outside the local challenge runtime, for example Application Insights/OpenTelemetry with dashboards and alerts.

Do not return raw exception messages to HTTP clients.

### Background Sync Errors

Background synchronization failures should not use HTTP `ProblemDetails`; those are only for request/response API errors.

Current approach:

- Log the exception with a stable sync event id.
- Store the failure in sync state through `SyncEvent.LastError`.
- Mark sync status as `FailedRetryable`, `FailedTerminal`, or `Pending` depending on retry policy.
- Keep enough correlation data to connect logs, sync event rows, and affected entities.

## Test Structure

Current test layers:

- `TodoApi.Tests/Controllers`: controller-level tests. Existing controller tests may use EF InMemory where already present.
- `TodoApi.Tests/Services`: service unit tests using fake repositories.
- `TodoApi.Tests/Models`: entity/property serialization tests.
- `TodoApi.Tests/Integration`: controller + service integration tests with only repositories mocked/faked.
- `TodoApi.Tests/Middleware`: HTTP middleware behavior tests.
- `TodoApi.Tests/Sync`: sync event publisher and outbound/inbound job tests.
- `TodoApi.Tests/E2E`: in-process API-to-API sync tests with `WebApplicationFactory`.

Integration tests should avoid EF InMemory for the first version. Mock or fake only repository interfaces and exercise controller + service behavior together.

E2E sync tests are allowed to use an isolated EF InMemory database for the in-process `TodoApi` host. They should not wait for Hangfire timers. Instead, call the local HTTP API, read the created `SyncEvent`, execute the relevant sync job directly, and verify the opposite API over HTTP.

## Verification

Run before finishing changes:

```powershell
dotnet build --no-restore --verbosity minimal
dotnet test --no-restore --verbosity minimal
```

If dependencies or assets are stale, run:

```powershell
dotnet restore
```

Avoid running `dotnet build` and `dotnet test` at the same time for this solution because they can contend for the same `TodoApi` build outputs.

## Notes For Future Synchronization Work

- Treat `TodoApi` as the local system and `ExternalApi` as the fake external system.
- Use `source_id` as the correlation field, but preserve each system's native ID.
- Use local `ExternalId` to store the external API resource id needed for PATCH/DELETE.
- Do not assume ID types match: local IDs are `long`, external IDs are `string`.
- Normalize item names carefully: local `Name` equals external `description`.
- Normalize completion carefully: local `IsCompleted` equals external `completed`.
- External deletes are hard deletes in the fake API. If sync needs deletion detection later, add explicit local tracking rather than inferring from absent rows without a policy.
- Local deletes are soft deletes for recovery. Synchronization logic must decide separately whether and when to propagate soft-deleted local records as hard deletes to the external API.
- Outbound create/update `404` responses should trigger inbound reconciliation. Complete the outbound `SyncEvent` only when reconciliation confirms the local entity, or the parent list for item events, is now soft-deleted because the external record disappeared.
- Outbound item create/update events must wait in `Pending` when the parent TodoList does not yet have `ExternalId`.
- Outbound update events without `ExternalId` may wait in `Pending` only when a matching create event is still pending or retryable; otherwise mark them `FailedTerminal`.
- Outbound create/update events for soft-deleted entities, or items whose parent list is soft-deleted, are obsolete and should complete without external calls.
- Outbound deletes without `ExternalId` should complete successfully because there is no known external target.
- Ambiguous outbound create failures, such as timeout, transport error, external `5xx`, or external `409`, should run inbound reconciliation by `source_id` before retrying or failing.
- External `GET /todolists` is the most efficient read because it returns lists and items together.
- Local `GET /api/todolists/{id}` returns the selected list with its active items. Local `GET /api/todolists` still returns lists without embedded items.

## Hangfire Sync

TodoApi uses Hangfire for synchronization execution:

- Outbound sync is event-driven. Local mutations create `SyncEvents` and enqueue outbound jobs.
- Inbound sync is polling-based through a recurring Hangfire job every 1 minute because ExternalApi has no webhook/events.
- Inbound sync disables Hangfire automatic retries. A failed inbound run should fail once and wait for the next 1-minute polling execution instead of accumulating scheduled retry jobs.
- Inbound sync disables concurrent execution with a 300-second lock timeout so slow polls do not overlap with the next recurring run.
- A recovery recurring job runs every 1 minute and re-enqueues retryable `Pending`, `FailedRetryable`, and legacy `Failed` sync events while attempts are below the max.
- Outbound create/update `404` responses run inbound reconciliation immediately. If inbound confirms external deletion through local soft delete, the outbound event is completed instead of retried to the max attempt limit.
- `FailedTerminal` events are not re-enqueued automatically.
- `todoapi-sync-repair-manual` is a manual-only repair job. It is registered in Hangfire with `Cron.Never()` so it appears in the dashboard but never runs on a timer.
- Inbound can restore a local soft-deleted list/item only when the local `DeletedAt` is earlier than the external `created_at`. In that case it clears `IsDeleted`/`DeletedAt` and refreshes the entity fields from the external payload.
- If `DeletedAt >= external.created_at`, inbound leaves the local entity soft-deleted and does not import child items under it. This can be a normal local-delete/outbound-delete race, so do not log it as a conflict.

Manual repair job:

- Run from Hangfire Dashboard: `Recurring Jobs -> todoapi-sync-repair-manual -> Trigger now`.
- Runs inbound reconciliation first, best effort, so existing external records can be linked before creating missing outbound events.
- Reopens exhausted retryable sync events with `Attempts >= 5` by setting them back to `Pending`, resetting `Attempts` to `0`, clearing `LastError`, and enqueuing outbound processing again.
- Creates missing `Created` `SyncEvents` for active local `TodoList` and `Item` rows that have no `ExternalId` and no existing create event.
- Does not reopen `Completed`, `Processing`, or `FailedTerminal` events.
- Does not run automatically; it is an operational tool for manual recovery cases such as a local row without a `SyncEvent` or an event that exhausted attempts while `ExternalApi` was unavailable.

Development dashboard:

```http
/hangfire
```

External API configuration lives under:

```json
"ExternalApi": {
  "BaseUrl": "http://localhost:5090",
  "TimeoutSeconds": 10,
  "MaxRetries": 3
}
```

The fake `ExternalApi` includes a local test extension for standalone item creation:

```http
POST /todolists/{todolistId}/todoitems
```

This endpoint is not part of the original challenge contract; it exists to exercise outbound `ItemCreated`.

Inbound sync is the recovery path when local application data is cleared but `ExternalApi` still has records:

- If the local database schema still exists and only application rows are cleared, the next inbound polling run imports the current external lists/items again.
- If the entire local database is dropped, run EF migrations first and then let inbound polling import from `ExternalApi`.
- This only restores records that still exist in `ExternalApi`; the fake external API is in-memory and resets on process restart or `POST /__test/reset`.
- After inbound imports or updates local data, TodoApi publishes SignalR events so connected frontends can refresh automatically.

## Local SignalR Realtime

`TodoApi` uses self-hosted ASP.NET Core SignalR for local realtime notifications. Do not add Azure SignalR resources.

Hub endpoint:

```http
/hubs/todo-updates
```

Client event name:

```text
todoUpdated
```

Server implementation:

- Hub: `TodoApi.Realtime.TodoUpdatesHub`
- Publisher abstraction: `TodoApi.Realtime.ITodoRealtimeNotifier`
- SignalR publisher: `TodoApi.Realtime.SignalRTodoRealtimeNotifier`
- No-op test/default fallback: `TodoApi.Realtime.NoOpTodoRealtimeNotifier`

Payload contract:

- `eventType`: `TodoListCreated`, `TodoListUpdated`, `TodoListDeleted`, `ItemCreated`, `ItemUpdated`, `ItemDeleted`, `InboundSyncCompleted`
- `entityType`: `TodoList`, `Item`, or `Sync`
- `entityId`
- `todoListId`
- `source`: `LocalApi` or `InboundSync`
- `occurredAt`
- `payload`
- `correlation_id`

Publish realtime events when:

- HTTP API requests create/update/delete TodoLists.
- HTTP API requests create/update/delete Items.
- Inbound sync imports/updates/soft-deletes TodoLists or Items.
- Inbound sync finishes, using `InboundSyncCompleted`.

CORS for browser clients is configured by:

```json
"Realtime": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:4200",
    "http://localhost:5173"
  ]
}
```

Keep the hub self-hosted with `TodoApi`. If a future frontend runs on a different local port, add that origin to `Realtime:AllowedOrigins`.
