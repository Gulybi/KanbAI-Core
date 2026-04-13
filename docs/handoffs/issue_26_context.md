# Context Handoff: Issue #26 — Generate Initial EF Core Migration and Apply to Database

## Title & Business Value

**Title:** Generate Initial EF Core Migration and Apply to Database
**Who:** The engineering team and any developer who needs a working, schema-correct SQL Server database to build and test application features against.
**Why:** All seven domain entities and their Fluent API configurations have been incrementally developed and audited across issues #21–#25. However, the database schema has never been consolidated into a single authoritative migration or applied to the actual SQL Server instance. Until the migration is generated, reviewed, and applied, the database is unusable for application development, integration testing, and feature work in subsequent milestones. This issue is the final gate in the "Database Models & EF Core Setup" milestone — completing it means the persistence layer is fully operational.

## Current State vs. Desired State

### Current State

- Five incremental EF Core migrations exist in `KanbAI-Core/KanbAI-Core/Migrations/`:
  - `InitialCreate` — empty initial migration (created during Issue #7)
  - `AddUserEntity` — `Users` table (Issue #21)
  - `AddProjectAndProjectMemberEntities` — `Projects` and `ProjectMembers` tables (Issue #22)
  - `AddBoardColumnAndKanbanTaskEntities` — `BoardColumns` and `KanbanTasks` tables (Issue #23)
  - `AddAssetAndTaskCommentEntities` — `Assets` and `TaskComments` tables (Issue #24)
  - `ApplicationDbContextModelSnapshot.cs` — current snapshot
- Issue #25 added a `Description` property to the `Project` entity and configured `HasMaxLength(500)` in `ProjectConfiguration`. This change is **not yet captured in any migration** — the model snapshot is intentionally out of sync (deferred to this issue).
- `ApplicationDbContext` is registered via `AddPersistence` extension method in `ServiceCollectionExtensions.cs`, using `UseSqlServer` with `DefaultConnection`.
- `DesignTimeDbContextFactory` exists at `Data/DesignTimeDbContextFactory.cs` to enable EF CLI tooling.
- The connection string is defined in `appsettings.Development.json` pointing to a local SQL Server instance with database name `KanbAI`.
- Production `appsettings.json` does not contain a `ConnectionStrings` section (relies on environment-specific configuration).
- The SQL Server database has **not been created or updated** with the current schema.

### Desired State

- A migration exists that captures the **complete and final domain model** — all seven entities (`User`, `Project`, `ProjectMember`, `BoardColumn`, `KanbanTask`, `Asset`, `TaskComment`) with every property, constraint, index, and relationship as configured in the Fluent API configurations audited in Issue #25.
- The `Project.Description` column (added in Issue #25) is included in the migration.
- The migration has been **reviewed** by the developer to confirm the generated schema matches the intended domain model (correct column types, nullability, max lengths, indexes, foreign keys, and delete behaviours).
- The migration has been **applied to the local SQL Server database** (`KanbAI` on the configured server), creating all tables with the correct schema.
- The model snapshot (`ApplicationDbContextModelSnapshot.cs`) is in sync with the domain model — no pending model changes remain.
- All existing tests continue to pass after the migration is generated and applied.

## Milestone Context

**Milestone:** Database Models & EF Core Setup (Domain Entities)

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 21 | Implement User Domain Entity | Closed | Prerequisite — delivered `User`, `UserRole`, `UserConfiguration` |
| 22 | Implement Project Domain and N:M Relationship | Closed | Prerequisite — delivered `Project`, `ProjectMember`, `ProjectRole`, configurations |
| 23 | Implement Kanban Board Domain (Columns & Tasks) | Closed | Prerequisite — delivered `BoardColumn`, `KanbanTask`, configurations |
| 24 | Implement Attachments (Assets) and Comments Domains | Closed | Prerequisite — delivered `Asset`, `TaskComment`, `ProcessingStatus`, configurations |
| 25 | Configure AppDbContext with Fluent API | Open | Prerequisite — audited all configurations, added `Project.Description` |
| **26** | **Generate Initial EF Core Migration and Apply to Database** | **Open (this issue)** | **Final milestone gate** — consolidates/applies all schema work |

Issue #26 is the **final issue** in the "Database Models & EF Core Setup" milestone. Every entity, enum, configuration, and DbContext change from issues #21–#25 must be reflected in a working database after this issue is complete.

## Acceptance Criteria

### Migration Generation

- [ ] A migration is generated that captures the complete domain model, including the `Project.Description` property added in Issue #25.
- [ ] The generated migration file is reviewed and confirmed to produce the correct schema for all seven entity tables (`Users`, `Projects`, `ProjectMembers`, `BoardColumns`, `KanbanTasks`, `Assets`, `TaskComments`).
- [ ] The model snapshot (`ApplicationDbContextModelSnapshot.cs`) is in sync with the current domain model after migration generation — no pending model changes remain.

### Schema Verification (per entity)

- [ ] The `Users` table contains columns: `Id` (PK, uniqueidentifier), `Name` (required, max length), `Email` (required, max length, unique index), `PasswordHash` (required), `Role` (required, default value), `CreatedAt`, `UpdatedAt`.
- [ ] The `Projects` table contains columns: `Id` (PK, uniqueidentifier), `Name` (required, max length), `Description` (nullable, max length), `CreatedAt`, `UpdatedAt`.
- [ ] The `ProjectMembers` table contains columns: `Id` (PK, uniqueidentifier), `ProjectId` (FK to Projects, cascade delete), `UserId` (FK to Users, restrict delete), `Role` (required, default value), `CreatedAt`, `UpdatedAt` — with a unique index on `(ProjectId, UserId)`.
- [ ] The `BoardColumns` table contains columns: `Id` (PK, uniqueidentifier), `Name` (required, max length), `ColorCode` (nullable, max length), `ColumnOrder` (required), `ProjectId` (FK to Projects, cascade delete), `CreatedAt`, `UpdatedAt`.
- [ ] The `KanbanTasks` table contains columns: `Id` (PK, uniqueidentifier), `Title` (required, max length), `Content` (nullable), `TaskOrder` (required), `ColumnId` (FK to BoardColumns, cascade delete), `AssignedId` (nullable FK to Users, set null on delete), `CreatedAt`, `UpdatedAt`.
- [ ] The `Assets` table contains columns: `Id` (PK, uniqueidentifier), `FileName` (required, max length), `StorageKey` (required, max length, unique index), `ThumbnailKey` (nullable, max length), `MimeType` (required, max length), `FileSize` (required), `ProcessingStatus` (required, default value), `KanbanTaskId` (FK to KanbanTasks, cascade delete), `CreatedAt`, `UpdatedAt`.
- [ ] The `TaskComments` table contains columns: `Id` (PK, uniqueidentifier), `Content` (required), `KanbanTaskId` (FK to KanbanTasks, cascade delete), `AuthorId` (FK to Users, restrict delete), `CreatedAt`, `UpdatedAt`.

### Database Application

- [ ] The migration is successfully applied to the local SQL Server database without errors.
- [ ] All seven entity tables exist in the database after migration application.
- [ ] Foreign key constraints, indexes, and delete behaviours are enforced at the database level as configured in the Fluent API.

### Regression Safety

- [ ] The solution compiles with zero errors and zero warnings after migration generation.
- [ ] All existing automated tests continue to pass after migration generation and application.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | Migration captures complete model incl. Description | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | Migration reviewed for correct schema | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | Model snapshot in sync | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | Users table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | Projects table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | ProjectMembers table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | BoardColumns table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | KanbanTasks table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | Assets table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | TaskComments table schema correct | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | Migration applied to SQL Server without errors | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | All seven tables exist in database | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | FK constraints, indexes, delete behaviours enforced | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | Solution compiles with 0 errors/warnings | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | All existing tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
