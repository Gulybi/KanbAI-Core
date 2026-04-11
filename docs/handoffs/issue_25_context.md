# Context Handoff: Issue #25 — Configure AppDbContext with Fluent API

## Title & Business Value

**Title:** Configure AppDbContext with Fluent API
**Who:** The engineering team and any future developer who relies on database-level integrity guarantees to prevent invalid application states.
**Why:** Fluent API configurations are the authoritative layer of defence for data integrity. Even if application-layer validation is bypassed (e.g., during bulk imports, manual SQL scripts, or future API endpoints that skip service-level checks), the database must independently enforce constraints such as uniqueness, referential integrity, required fields, maximum lengths, and cascading/restricted deletes. A complete and audited set of configurations ensures the domain model contract is honoured at the storage level.

## Current State vs. Desired State

### Current State

- `ApplicationDbContext` (in `Data/ApplicationDbContext.cs`) registers `DbSet` properties for all seven entities: `User`, `Project`, `ProjectMember`, `BoardColumn`, `KanbanTask`, `Asset`, and `TaskComment`.
- `OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)`, which auto-discovers all `IEntityTypeConfiguration<T>` implementations.
- `SaveChangesAsync` is overridden to auto-stamp `CreatedAt` and `UpdatedAt` on `BaseEntity` subclasses.
- Seven configuration classes already exist in `Data/Configurations/`:
  - `UserConfiguration.cs` — unique index on `Email`, max lengths, required fields.
  - `ProjectConfiguration.cs` — max lengths, required fields.
  - `ProjectMemberConfiguration.cs` — surrogate `Guid` primary key (from `BaseEntity`), unique index on `(ProjectId, UserId)`, cascade from `Project`, restrict from `User`.
  - `BoardColumnConfiguration.cs` — cascade from `Project`, required FK, max length, column order.
  - `KanbanTaskConfiguration.cs` — cascade from `BoardColumn`, `SetNull` for optional `AssignedId` FK to `User`.
  - `AssetConfiguration.cs` — unique index on `StorageKey`, cascade from `KanbanTask`, max lengths, required fields.
  - `TaskCommentConfiguration.cs` — cascade from `KanbanTask`, restrict from `User` (author), required content.
- Existing migrations already reflect these configurations: `InitialCreate`, `AddUserEntity`, `AddProjectAndProjectMemberEntities`, `AddBoardColumnAndKanbanTaskEntities`, `AddAssetAndTaskCommentEntities`.

### Desired State

- All Fluent API configurations are **audited for completeness** and verified to match the intended domain rules described across issues #21–#24.
- Every entity configuration explicitly declares: primary key, required/optional properties, max lengths (where applicable), indexes (unique and non-unique), relationship cardinality, foreign key properties, and delete behaviours — leaving nothing to EF Core convention inference for critical rules.
- The `ApplicationDbContext` class remains clean with no inline configuration; all rules live in dedicated `IEntityTypeConfiguration<T>` classes.

### Key Discrepancy: ProjectMember Primary Key Strategy

The original issue body states: *"Configure the composite primary key for ProjectMembers (ProjectId, UserId)."* The current implementation uses a **surrogate `Guid` primary key** inherited from `BaseEntity`, combined with a **unique index on `(ProjectId, UserId)`**. Both approaches enforce the one-membership-per-user-per-project business rule. The engineering team should evaluate whether changing to a composite primary key is warranted or whether the surrogate key with unique index (consistent with all other entities) is the preferred approach for this project.

## Milestone Context

**Milestone:** Database Models & EF Core Setup (Domain Entities)

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 21 | Implement User Domain Entity | Closed | Prerequisite (delivered `User`, `UserRole`, `UserConfiguration`) |
| 22 | Implement Project Domain and N:M Relationship | Closed | Prerequisite (delivered `Project`, `ProjectMember`, `ProjectRole`, `ProjectMemberConfiguration`) |
| 23 | Implement Kanban Board Domain (Columns & Tasks) | Closed | Prerequisite (delivered `BoardColumn`, `KanbanTask`, configurations) |
| 24 | Implement Attachments (Assets) and Comments Domains | Closed | Prerequisite (delivered `Asset`, `TaskComment`, configurations) |
| 25 | **Configure AppDbContext with Fluent API** | **Open (this issue)** | Consolidation/audit of all configurations from #21–#24 |
| 26 | Generate Initial EF Core Migration and Apply to Database | Open | Depends on #25 |

Issue #25 is a consolidation step. The individual entity configurations were incrementally built during issues #21–#24. This issue serves as the quality gate to verify all configurations are complete, consistent, and explicitly declared before the final migration (#26) is generated.

## Acceptance Criteria

### ApplicationDbContext Structure

- [ ] `ApplicationDbContext` exposes a `DbSet<T>` property for each domain entity: `User`, `Project`, `ProjectMember`, `BoardColumn`, `KanbanTask`, `Asset`, and `TaskComment`.
- [ ] `OnModelCreating` applies all entity configurations from the assembly without inline configuration logic.
- [ ] `SaveChangesAsync` automatically sets `CreatedAt` on new entities and `UpdatedAt` on modified entities.

### User Constraints

- [ ] `User.Email` is required and has a maximum length constraint.
- [ ] `User.Email` is enforced as unique at the database level.
- [ ] `User.Name` is required and has a maximum length constraint.
- [ ] `User.PasswordHash` is required.
- [ ] Deleting a `User` who has authored comments is prevented (restricted delete behaviour).

### Project Constraints

- [ ] `Project.Name` is required and has a maximum length constraint.
- [ ] `Project.Description` has a maximum length constraint.

### ProjectMember Constraints

- [ ] A `ProjectMember` cannot exist without a parent `Project` (required relationship).
- [ ] A `ProjectMember` cannot exist without an associated `User` (required relationship).
- [ ] The combination of `ProjectId` and `UserId` is enforced as unique at the database level (one membership per user per project).
- [ ] Deleting a `Project` cascades the deletion to its `ProjectMember` records.
- [ ] Deleting a `User` who is a project member is prevented (restricted delete behaviour), ensuring membership records are not silently lost.

### BoardColumn Constraints

- [ ] A `BoardColumn` cannot exist without a parent `Project` (required relationship).
- [ ] `BoardColumn.Name` is required and has a maximum length constraint.
- [ ] Deleting a `Project` cascades the deletion to its `BoardColumn` records.

### KanbanTask Constraints

- [ ] A `KanbanTask` cannot exist without a parent `BoardColumn` (required relationship).
- [ ] `KanbanTask.Title` is required and has a maximum length constraint.
- [ ] The assigned user on a `KanbanTask` is optional; the foreign key reference is nullable.
- [ ] Deleting a `BoardColumn` cascades the deletion to its `KanbanTask` records.
- [ ] If the assigned `User` is deleted, the assignment reference on the task is set to null rather than deleting the task.

### Asset Constraints

- [ ] An `Asset` cannot exist without a parent `KanbanTask` (required relationship).
- [ ] `Asset.StorageKey` is enforced as unique at the database level.
- [ ] `Asset.FileName` is required and has a maximum length constraint.
- [ ] `Asset.MimeType` is required and has a maximum length constraint.
- [ ] `Asset.ProcessingStatus` is required.
- [ ] `Asset.ThumbnailKey` is optional (nullable).
- [ ] Deleting a `KanbanTask` cascades the deletion to its `Asset` records.

### TaskComment Constraints

- [ ] A `TaskComment` cannot exist without a parent `KanbanTask` (required relationship).
- [ ] A `TaskComment` cannot exist without an authoring `User` (required relationship).
- [ ] `TaskComment.Content` is required.
- [ ] Deleting a `KanbanTask` cascades the deletion to its `TaskComment` records.
- [ ] Deleting a `User` who has authored comments is prevented (restricted delete behaviour).

### Configuration Conventions

- [ ] Each entity has a dedicated configuration class following the `IEntityTypeConfiguration<T>` pattern located in `Data/Configurations/`.
- [ ] No entity relies solely on EF Core conventions for critical constraints (keys, required fields, relationships, delete behaviours, and unique indexes are all explicitly declared).
- [ ] The solution compiles and all existing tests continue to pass after any configuration changes.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | DbSet for each entity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | OnModelCreating applies assembly configs | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | Auto-stamp CreatedAt/UpdatedAt | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | User.Email required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | User.Email unique | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | User.Name required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | User.PasswordHash required | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | User delete restricted when has comments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | Project.Name required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | Project.Description max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | ProjectMember requires Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | ProjectMember requires User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | ProjectMember (ProjectId, UserId) unique | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | Cascade: Project → ProjectMembers | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | Restrict: User → ProjectMembers | ✅ | ✅ | ✅ | ✅ | ✅ |
| 16 | BoardColumn requires Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 17 | BoardColumn.Name required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 18 | Cascade: Project → BoardColumns | ✅ | ✅ | ✅ | ✅ | ✅ |
| 19 | KanbanTask requires BoardColumn | ✅ | ✅ | ✅ | ✅ | ✅ |
| 20 | KanbanTask.Title required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 21 | KanbanTask assigned user is optional | ✅ | ✅ | ✅ | ✅ | ✅ |
| 22 | Cascade: BoardColumn → KanbanTasks | ✅ | ✅ | ✅ | ✅ | ✅ |
| 23 | SetNull: User deletion → KanbanTask assignment | ✅ | ✅ | ✅ | ✅ | ✅ |
| 24 | Asset requires KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 25 | Asset.StorageKey unique | ✅ | ✅ | ✅ | ✅ | ✅ |
| 26 | Asset.FileName required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 27 | Asset.MimeType required with max length | ✅ | ✅ | ✅ | ✅ | ✅ |
| 28 | Asset.ProcessingStatus required | ✅ | ✅ | ✅ | ✅ | ✅ |
| 29 | Asset.ThumbnailKey optional | ✅ | ✅ | ✅ | ✅ | ✅ |
| 30 | Cascade: KanbanTask → Assets | ✅ | ✅ | ✅ | ✅ | ✅ |
| 31 | TaskComment requires KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 32 | TaskComment requires authoring User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 33 | TaskComment.Content required | ✅ | ✅ | ✅ | ✅ | ✅ |
| 34 | Cascade: KanbanTask → TaskComments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 35 | Restrict: User → TaskComments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 36 | Dedicated IEntityTypeConfiguration per entity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 37 | No convention-only critical constraints | ✅ | ✅ | ✅ | ✅ | ✅ |
| 38 | Solution compiles, tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
