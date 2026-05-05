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

### ExternalApi

`ExternalApi` does not use a database. It stores deterministic data in memory through a singleton store and resets on process restart.

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

## Synchronized Models

There are two logical entities to synchronize between `TodoApi` and `ExternalApi`.

### Todo List

Local model: `TodoApi.Models.TodoList`

Properties:

- `Id: long`
- `SourceId: string?`, JSON `source_id`
- `Name: string`
- `CreatedAt: DateTimeOffset`, JSON `created_at`
- `UpdatedAt: DateTimeOffset`, JSON `updated_at`
- `IsDeleted: bool`, JSON `is_deleted`
- `DeletedAt: DateTimeOffset?`, JSON `deleted_at`

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
- Local `TodoList` does not embed items in its API response; external `TodoList` does.

### Todo Item

Local model: `TodoApi.Models.Item`

Properties:

- `Id: long`
- `SourceId: string?`, JSON `source_id`
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

- `PATCH /todolists/{todolistId}/todoitems/{todoitemId}`
- `DELETE /todolists/{todolistId}/todoitems/{todoitemId}`

Todo item contracts:

- `CreateTodoItemBody`: `source_id`, `description`, `completed`
- `UpdateTodoItemBody`: `description`, `completed`

External API behavior:

- `GET /todolists` returns lists with embedded items.
- `POST /todolists` creates a list and optional items in one request.
- There is no external endpoint to create a standalone item under an existing list.
- External IDs are deterministic strings like `ext-list-001` and `ext-item-001`.
- Seed timestamps and runtime mutation timestamps are deterministic for repeatable sync tests.

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

- Use `ApiErrorEventIds` for stable log event IDs.
- Log controlled HTTP errors at warning level.
- Log unhandled HTTP exceptions at error level.
- Include relevant route IDs and `traceId` in log messages.

Do not return raw exception messages to HTTP clients.

### Background Sync Errors

Background synchronization is not implemented yet. When it is added, do not use HTTP `ProblemDetails` for sync-job failures.

Expected future approach:

- Log the exception with a stable sync event id.
- Store the failure in sync state, for example `SyncEvent.LastError`.
- Mark sync status as `Failed` or `Pending` depending on retry policy.
- Keep enough correlation data to connect logs, sync event rows, and affected entities.

## Test Structure

Current test layers:

- `TodoApi.Tests/Controllers`: controller-level tests. Existing controller tests may use EF InMemory where already present.
- `TodoApi.Tests/Services`: service unit tests using fake repositories.
- `TodoApi.Tests/Models`: entity/property serialization tests.
- `TodoApi.Tests/Integration`: controller + service integration tests with only repositories mocked/faked.
- `TodoApi.Tests/Middleware`: HTTP middleware behavior tests.

Integration tests should avoid EF InMemory for the first version. Mock or fake only repository interfaces and exercise controller + service behavior together.

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
- Do not assume ID types match: local IDs are `long`, external IDs are `string`.
- Normalize item names carefully: local `Name` equals external `description`.
- Normalize completion carefully: local `IsCompleted` equals external `completed`.
- External deletes are hard deletes in the fake API. If sync needs deletion detection later, add explicit local tracking rather than inferring from absent rows without a policy.
- Local deletes are soft deletes for recovery. Synchronization logic must decide separately whether and when to propagate soft-deleted local records as hard deletes to the external API.
- External `GET /todolists` is the most efficient read because it returns lists and items together.
- Local API currently requires separate item reads per list.
