# KanbAI-Core — Backend Project Summary

## 1. Project Purpose & Thesis Context

**KanbAI** is the backend service for a Kanban-style project management platform, built as part of a **university thesis** that measures the efficiency, code quality, and usability of AI-based coding assistants (Cursor, GitHub Copilot, Claude) when building real production-grade features.

Two parallel development methodologies are deliberately used:

1. **Reference Module (manual, no AI):** JWT-based registration & authentication — baseline measurement.
2. **AI-Driven Modules:** Complex business logic (Kanban drag-and-drop, async file uploads, SignalR, member management) — built with AI assistance, using the handoff documents in [docs/handoffs/](handoffs/) (one context doc + one tech spec per GitHub issue).

The outcome is a stable RESTful Web API that serves the Angular frontend (expected at `http://localhost:4200`), persists state in SQL Server, and pushes real-time updates over SignalR.

---

## 2. Tech Stack & Why

| Concern | Technology | Rationale |
|---|---|---|
| Framework | **.NET 10 Web API (C#)** — [KanbAI-Core.csproj](../KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj) | Modern, strongly-typed, mature framework; the thesis author's baseline stack. Uses file-scoped namespaces, `record` DTOs, nullable reference types, primary constructors. |
| ORM | **Entity Framework Core 10** + Fluent API | Parameterized queries (SQLi-safe), rich relationship modeling (N:M via explicit `ProjectMember`), migration-based schema versioning. |
| Database | **Microsoft SQL Server** (LocalDB in dev) | Enterprise-standard relational DB, easy local setup via Trusted Connection. |
| Auth | **JWT Bearer** (`Microsoft.AspNetCore.Authentication.JwtBearer`) + **BCrypt** (work factor 12) | Stateless tokens for API + SignalR; BCrypt for salted password hashing. |
| Real-time | **SignalR** | Broadcasts kanban/task/column/member/attachment changes to clients in project-scoped groups. Supports JWT via query string (`?access_token=`) because browsers cannot set headers on WebSocket. |
| File storage | Local disk (`wwwroot/uploads`) behind `FileStorageOptions` | Initial impl per thesis scope; **MinIO integration planned** per README. |
| API docs | **OpenAPI** (`Microsoft.AspNetCore.OpenApi`) + **Scalar** UI | Modern alternative to Swagger. Loaded via `NoInlining` helper to avoid WDAC DLL loading issues on enterprise machines — see [WebApplicationExtensions.cs](../KanbAI-Core/KanbAI-Core/Extensions/WebApplicationExtensions.cs). |
| Error handling | `IExceptionHandler` global handler | Returns sanitized `ApiResponse` in prod, detailed diagnostics in dev — see [GlobalExceptionHandler.cs](../KanbAI-Core/KanbAI-Core/Middleware/GlobalExceptionHandler.cs). |

Auth package list also includes `Microsoft.AspNetCore.Authentication.Negotiate` — this is a Windows-hosting artifact; integration tests [explicitly strip it out](../.claude/rules/integration-testing.md) because `TestServer` can't provide `IConnectionItemsFeature`.

---

## 3. Actors / Use Cases

**Primary actors:**
- **Unauthenticated visitor** → `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/health`
- **Authenticated user (UserRole = Member | Admin)** — JWT holder; joins a project via invitation.
- **Project Owner** (`ProjectRole.Owner`) — CRUD the project, add/remove members, delete columns/tasks. Cannot remove the **last owner**.
- **Project Member** (`ProjectRole.Member`) — full board collaboration: create/move tasks, edit descriptions, upload/delete attachments, but cannot delete the project or manage other members.
- **Angular Frontend** — sole intended API consumer; CORS locked to `http://localhost:4200` with credentials.

**Use cases:**
1. **Register / Log in** and receive JWT + profile.
2. **Create a project** (caller becomes Owner).
3. **Invite members** by `UserId` or `Email` (exclusive, required) — [AddMemberDto](../KanbAI-Core/KanbAI-Core/DTOs/AddMemberDto.cs).
4. **Manage board columns** — create (auto-appended `ColumnOrder`), list, delete.
5. **Manage tasks** — create in a column (validates assignee is a project member), reorder vertically, move horizontally across columns, set/update/clear description (≤ 10 000 chars), list all project tasks to hydrate the board on page load.
6. **Upload attachments** on tasks — async status lifecycle (`Pending → Processing → Completed/Failed`) broadcast over SignalR; list, download, delete.
7. **Real-time collaboration** — every mutation is pushed to `project_{projectId}` SignalR group so other connected clients update instantly.

---

## 4. Architecture Overview

Classic **layered ASP.NET architecture**, no CQRS/MediatR (YAGNI per [code-standards.md](../.claude/rules/code-standards.md)):

```
Controllers (thin)
    └── Services (business logic, authorization, transactions, broadcasts)
        └── ApplicationDbContext (EF Core)  ─┬─► SQL Server
                                             └─► SignalR IHubContext<KanbanHub>
```

**Key cross-cutting concerns** wired in [Program.cs](../KanbAI-Core/KanbAI-Core/Program.cs) via extension methods in [ServiceCollectionExtensions.cs](../KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs):

- `AddPersistence` — EF Core with SQL Server, `DefaultConnection` string.
- `AddApiInfrastructure` — controllers, OpenAPI, ProblemDetails, `GlobalExceptionHandler`.
- `AddAuthServices` — authorization without a fallback policy; every controller declares `[Authorize]` or `[AllowAnonymous]` explicitly.
- `AddCorsPolicy` — named `"AllowAngularFrontend"`, allows the configured origins, methods GET/POST/PUT/DELETE/PATCH, `AllowAnyHeader()`, credentials enabled (any-header was added to fix issue #77 — SignalR `x-requested-with` preflight).
- `AddSignalRInfrastructure` — maps [KanbanHub.cs](../KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs) at `/hubs/kanban`.
- `AddFileStorage` — binds & validates `FileStorageOptions` (fail-fast at startup via `ValidateOnStart`).

JWT bearer options include an `OnMessageReceived` event so SignalR clients can pass the token as `?access_token=` for WebSocket upgrades.

---

## 5. Domain Model (Entities)

All entities inherit `BaseEntity { Guid Id, DateTimeOffset CreatedAt, UpdatedAt }`. Timestamps are set automatically in `ApplicationDbContext.SaveChangesAsync`.

| Entity | Key properties | Relationships |
|---|---|---|
| [User](../KanbAI-Core/KanbAI-Core/Models/Entities/User.cs) | Name (150), Email (256, unique), PasswordHash, `UserRole` (Member/Admin, default Member) | 1:N ProjectMembership, AssignedTasks, AuthoredComments |
| [Project](../KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs) | Name (200), Description? (500) | 1:N Members, Columns (cascade delete) |
| [ProjectMember](../KanbAI-Core/KanbAI-Core/Models/Entities/ProjectMember.cs) | ProjectId, UserId, `ProjectRole` (Member/Owner) | Unique composite index (ProjectId, UserId). Explicit N:M join. Project cascade-deletes, User restrict. |
| [BoardColumn](../KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs) | Name (100), ColorCode? (20), ColumnOrder (int) | Belongs to Project (cascade), has many Tasks |
| [KanbanTask](../KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs) | Title (200), Content? (≤10 000), TaskOrder (int), ColumnId, AssignedId? | Column (cascade), AssignedUser (`OnDelete.SetNull`), has Assets + Comments |
| [Asset](../KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs) | FileName (255), StorageKey (1024, unique), ThumbnailKey? (1024), MimeType (256), FileSize, `ProcessingStatus` (Pending/Processing/Completed/Failed, default Pending) | Belongs to KanbanTask (cascade) |
| [TaskComment](../KanbAI-Core/KanbAI-Core/Models/Entities/TaskComment.cs) | Content (required) | KanbanTask (cascade), Author (User, restrict) |

**Ordering invariants:** `ColumnOrder` is dense (0..N-1) per project; `TaskOrder` is dense per column. `TaskService.MoveTaskAsync` carefully shifts siblings on move to preserve the invariant for both same-column and cross-column moves.

Migrations are tracked under [Migrations/](../KanbAI-Core/KanbAI-Core/Migrations/) (6 migrations as of this branch; timestamped in the year 2026 because of the test environment clock).

---

## 6. Services / Business Logic

Each service owns authorization, transactions, and SignalR broadcasting for its domain.

- **[AuthController](../KanbAI-Core/KanbAI-Core/Controllers/AuthController.cs)** — register (unique-email check, BCrypt hash, default role Member), login (BCrypt verify), both return `AuthResponseDto { Token, User }`. `[AllowAnonymous]`.
- **[ProjectService](../KanbAI-Core/KanbAI-Core/Services/Projects/ProjectService.cs)** — Create (creator → Owner), List user's projects, Get by id (membership enforced), Update/Delete (Owner-only), AddMember (by UserId OR Email, exclusive), RemoveMember (Owner-only, cannot remove last owner), ListMembers. Broadcasts: `ProjectUpdated`, `ProjectDeleted`, `MemberAdded`, `MemberRemoved`.
- **[ColumnService](../KanbAI-Core/KanbAI-Core/Services/Columns/ColumnService.cs)** — any project member can create/list/delete columns; auto-computes next `ColumnOrder`. Broadcasts: `ColumnCreated`, `ColumnDeleted`.
- **[TaskService](../KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs)** — Create (validates title, column exists, membership, optional assignee membership), Move (same-column reorder vs cross-column move with sibling `TaskOrder` shifting, cross-project rejected), Update/Clear Description (≤10 000 chars), GetProjectTasks (hydration for board page load — issue #82). Broadcasts: `TaskCreated`, `TaskMoved`, `TaskUpdated`.
- **[AssetService](../KanbAI-Core/KanbAI-Core/Services/Assets/AssetService.cs)** — Defense-in-depth filename sanitization (blocks `..`, `/`, `\`, invalid chars), extension allow-list, **MIME-extension cross-check**, size limit re-enforcement, generates `{guid:N}_{fileName}` storage key, writes through `Pending → Processing → Completed/Failed` state machine. Every state transition is broadcast: `AssetUploadStarted`, `AssetProcessing`, `AssetCompleted`, `AssetFailed`. Failure cleans up the partial file.
- **[AttachmentController](../KanbAI-Core/KanbAI-Core/Controllers/AttachmentController.cs)** — List attachments per task (filters to `Completed`), GET physical file (`inline` for images, `attachment` otherwise, with RFC 5987 filename encoding), DELETE (membership check, path-escape guard, removes file + DB record, broadcasts `AttachmentDeleted`). `[RequestSizeLimit]` at the pipeline level mirrors the service limit.
- **[KanbanHub](../KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs)** — `[Authorize]`. Clients call `JoinProjectGroup(projectId)` / `LeaveProjectGroup(projectId)`; group name is `project_{guid-lowercase}`. Validates Guid format, throws `HubException` on bad input.

All services use `.AsNoTracking()` for reads, `.Include(...)` chains for auth traversal (Task → Column → Project → Members), and parameterized logging templates.

---

## 7. API Endpoints

All under `api/` prefix. Every authenticated endpoint returns wrapped `ApiResponse<T> { Success, Message, Errors, Data }`.

### Auth — `[AllowAnonymous]`
- `POST /api/auth/register` — create user, returns JWT + profile.
- `POST /api/auth/login` — returns JWT + profile or 401.

### Health — `[AllowAnonymous]`
- `GET /api/health` — liveness probe.

### Projects — `[Authorize]`
- `POST /api/project` — create (caller = Owner).
- `GET /api/project` — list projects the user is a member of.
- `GET /api/project/{id}` — get one (404 if not member).
- `PUT /api/project/{id}` — update name/description (any member).
- `DELETE /api/project/{id}` — Owner only (403 otherwise).
- `POST /api/project/{projectId}/members` — add member by `UserId` XOR `Email`. Owner only; 400 on bad DTO / unknown email, 403 on non-owner.
- `DELETE /api/project/{projectId}/members/{userId}` — Owner only; 400 when trying to remove last owner.
- `GET /api/project/{projectId}/members` — list members (sorted Owner first then join date).

### Columns — `[Authorize]`
- `GET /api/column/project/{projectId}` — list ordered columns.
- `POST /api/column/project/{projectId}` — create column (optional `ColumnOrder`, defaults to append).
- `DELETE /api/column/{id}` — delete column (cascades tasks).

### Tasks — `[Authorize]`
- `POST /api/task/column/{columnId}` — create task, optional assignee (must be project member).
- `PUT /api/task/{taskId}/move` — `{ ColumnId, TaskOrder }`; handles same-column reorder and cross-column moves; rejects cross-project.
- `PUT /api/task/{taskId}/description` — set/update (trimmed, 1–10 000 chars).
- `DELETE /api/task/{taskId}/description` — clear description (204).
- `GET /api/task/project/{projectId}` — list all tasks in project, ordered by `ColumnId, TaskOrder`.

### Attachments — `[Authorize]`
- `POST /api/attachment/task/{taskId}` — multipart upload, 10 MB pipeline cap.
- `GET /api/attachment/task/{taskId}` — list completed attachments.
- `GET /api/attachment/{assetId}` — download/inline-render.
- `DELETE /api/attachment/{assetId}` — remove file + DB row.

### Real-time — `/hubs/kanban` (SignalR)
Client methods: `JoinProjectGroup`, `LeaveProjectGroup`.
Server-sent events (all to `project_{projectId}`):
`ProjectUpdated`, `ProjectDeleted`, `MemberAdded`, `MemberRemoved`, `ColumnCreated`, `ColumnDeleted`, `TaskCreated`, `TaskMoved`, `TaskUpdated`, `AssetUploadStarted`, `AssetProcessing`, `AssetCompleted`, `AssetFailed`, `AttachmentDeleted`.

---

## 8. Security Posture

Implements the rules codified in [.claude/rules/security-safety.md](../.claude/rules/security-safety.md):

- **No hardcoded prod secrets.** Dev JWT secret in [appsettings.Development.json](../KanbAI-Core/KanbAI-Core/appsettings.Development.json) only; prod-style secrets expected via env/secret manager.
- **BCrypt (work factor 12)** for passwords; hashes never leave the service layer.
- **JWT** with `ValidateIssuer/Audience/Lifetime/Signing`, `ClockSkew = 0`, HS256, 60-minute expiry in dev.
- **Authorization-by-default per controller** (`AddAuthServices` deliberately has no fallback policy, so every controller declares `[Authorize]`/`[AllowAnonymous]` — prevents the auth regressions described in issue #70).
- **CORS** restricted to configured origins with `AllowCredentials`; `AllowAnyHeader` intentionally enabled to support SignalR preflight (issue #77).
- **File upload hardening**: extension allow-list + MIME-vs-extension cross-check + size limit + server-generated storage key + path-traversal defense-in-depth (`GetFullPath` + `StartsWith` storage-root check in `AttachmentController.GetFile/DeleteFile`).
- **Mass-assignment protection**: DTOs are explicit `record` types; entities are never directly bound.
- **Global exception handler** sanitizes error messages in production.

---

## 9. Testing Infrastructure

The `KanbAI-Core.Tests` project is the second project in the solution. It uses `WebApplicationFactory<Program>` integration tests; `Program.cs` ends with `public partial class Program { }` so the test host can reference it.

[.claude/rules/integration-testing.md](../.claude/rules/integration-testing.md) documents the two known TestServer hazards and the standard mitigation pattern: strip Negotiate auth registrations, register a no-op `TestAuthHandler`, nullify `FallbackPolicy`, and optionally inject an `IStartupFilter` to test middleware (e.g., `GlobalExceptionHandler`) without modifying production code. `Testing:SkipScalar` config flag also disables the Scalar UI in tests to avoid WDAC DLL loading.

---

## 10. Development Process Artifacts

[docs/handoffs/](handoffs/) contains **paired docs per GitHub issue** (`issue_N_context.md` + `issue_N_tech_spec.md`), produced by the "product-manager → staff-engineer → developer → QA" subagent pipeline defined in the repo's `.claude/` config. Recent history (per `git log`):
- `#82` Task read endpoints (board hydration)
- `#83` Task description set/update/clear
- `#84` Delete attachment endpoint
- `#85` Fix 401→400/403 response codes on invite failures

Earlier milestone arcs: **Foundations (#6–#11)**, **Domain modeling (#21–#28)**, **Auth (#41–#42)**, **Project & Kanban business logic (#43–#47)**, **SignalR (#61–#63, #77)**, **Attachments (#65–#67, #80, #84)**, **Bug fixes (#69, #70, #85)**.

---

## TL;DR

A **.NET 10 + EF Core + SQL Server + SignalR** Kanban backend with JWT auth, per-project membership-based authorization, real-time group broadcasts, and asynchronous file attachments with a 4-state processing lifecycle. Deliberately layered and pragmatic (no CQRS/MediatR), with strict security rules around file uploads and path traversal. Built under a dual-methodology thesis comparing manual vs. AI-assisted development, with every feature traced in [docs/handoffs/](handoffs/).
