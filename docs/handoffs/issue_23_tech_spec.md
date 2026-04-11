# Technical Specification: Issue #23 - Implement Kanban Board Domain (Columns & Tasks)

## Overview

Create the `BoardColumn` and `KanbanTask` domain entities to model the visual Kanban board within a project. A `BoardColumn` represents an ordered, named column (e.g., "To Do", "In Progress", "Done") belonging to a project, while a `KanbanTask` represents a work item placed within a column, optionally assigned to a user. Both entities inherit from `BaseEntity` and follow the same patterns established by `Project` and `ProjectMember` in issue #22. No API endpoints or application-layer services are required at this stage — this is a domain-model-only issue that lays the structural backbone for all future task-level features (drag-and-drop reordering, assignment, comments, attachments).

## Database/Domain Design

### 1. `BoardColumn` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
namespace KanbAI_Core.Models.Entities;

public class BoardColumn : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public int ColumnOrder { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public ICollection<KanbanTask> Tasks { get; set; } = new List<KanbanTask>();
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- `Name` uses `string.Empty` default per project convention; the database requires a non-null value via EF configuration.
- `ColorCode` is nullable (`string?`) because the AC describes it as "an optional visual colour identifier". It stores a CSS-compatible color value (e.g., `#FF5733`, `blue`).
- `ColumnOrder` is a plain `int` representing the column's position on the board. No unique constraint is applied because during drag-and-drop reordering, temporary collisions are expected — the application layer will manage ordering consistency.
- `Project` navigation uses `null!` per the EF Core convention established in `ProjectMember`.
- `Tasks` is initialized to `new List<KanbanTask>()` to prevent null-reference when iterating before EF loads related data.

### 2. `KanbanTask` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
namespace KanbAI_Core.Models.Entities;

public class KanbanTask : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int TaskOrder { get; set; }

    public Guid ColumnId { get; set; }
    public BoardColumn Column { get; set; } = null!;

    public Guid? AssignedId { get; set; }
    public User? AssignedUser { get; set; }
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- Named `KanbanTask` (not `Task`) to avoid collision with `System.Threading.Tasks.Task` per AC #9.
- `Title` uses `string.Empty` default; required at the database level via EF configuration.
- `Content` is nullable (`string?`) because the AC describes it as "an optional detailed description". Mapped to `nvarchar(max)` by EF Core's default string mapping (no `HasMaxLength` applied).
- `TaskOrder` is a plain `int` for position within the column. Same rationale as `ColumnOrder` — no unique constraint applied.
- `ColumnId` is a required `Guid` FK. A task cannot exist without a parent column (AC #23).
- `AssignedId` is a nullable `Guid?` FK. A task may exist without an assigned user (AC #24).
- `AssignedUser` is nullable (`User?`) to match the nullable FK. EF Core will not use `null!` here since the navigation is genuinely optional.
- `Column` navigation uses `null!` per the established EF Core convention for required navigation properties.

### 3. `Project` Entity Update — Navigation Property

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

Add the following navigation property to satisfy AC #17 (navigation from Project to its columns):

```csharp
public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
```

**Updated `Project` class:**

```csharp
namespace KanbAI_Core.Models.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
}
```

### 4. `User` Entity Update — Navigation Property

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

Add the following navigation property to satisfy AC #18 (navigation from User to assigned tasks):

```csharp
public ICollection<KanbanTask> AssignedTasks { get; set; } = new List<KanbanTask>();
```

**Updated `User` class:**

```csharp
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Models.Entities;

public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<KanbanTask> AssignedTasks { get; set; } = new List<KanbanTask>();
}
```

### 5. EF Core Entity Configuration — `BoardColumnConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/BoardColumnConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.HasKey(bc => bc.Id);

        builder.Property(bc => bc.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(bc => bc.ColorCode)
            .HasMaxLength(20);

        builder.Property(bc => bc.ColumnOrder)
            .IsRequired();

        builder.HasOne(bc => bc.Project)
            .WithMany(p => p.Columns)
            .HasForeignKey(bc => bc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Constraint Rationale:**

| Property / Constraint | Type | Reason |
|-----------------------|------|--------|
| `Id` | Primary Key | Surrogate PK inherited from `BaseEntity`. |
| `Name` | Required, MaxLength(100) | Column name cannot be blank; 100 chars matches the reference schema `NVARCHAR(100)`. |
| `ColorCode` | Optional, MaxLength(20) | Nullable string for visual colour identifier; 20 chars accommodates CSS color values (e.g., `#FF5733`, `rebeccapurple`). |
| `ColumnOrder` | Required | Ordering position must always have a value. `int` is non-nullable by default; `IsRequired()` is explicit for documentation. |
| `ProjectId` FK | Cascade delete | AC #25: Deleting a project removes all its columns. |

**Note:** The `BoardColumn → Tasks` relationship is configured from the `KanbanTaskConfiguration` side (the dependent entity owns the FK), keeping relationship configuration in one place.

### 6. EF Core Entity Configuration — `KanbanTaskConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/KanbanTaskConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class KanbanTaskConfiguration : IEntityTypeConfiguration<KanbanTask>
{
    public void Configure(EntityTypeBuilder<KanbanTask> builder)
    {
        builder.HasKey(kt => kt.Id);

        builder.Property(kt => kt.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(kt => kt.Content)
            .HasColumnType("nvarchar(max)");

        builder.Property(kt => kt.TaskOrder)
            .IsRequired();

        builder.HasOne(kt => kt.Column)
            .WithMany(bc => bc.Tasks)
            .HasForeignKey(kt => kt.ColumnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(kt => kt.AssignedUser)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(kt => kt.AssignedId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

**Constraint Rationale:**

| Property / Constraint | Type | Reason |
|-----------------------|------|--------|
| `Id` | Primary Key | Surrogate PK inherited from `BaseEntity`. |
| `Title` | Required, MaxLength(200) | Task title cannot be blank; 200 chars matches reference schema `NVARCHAR(200)`. |
| `Content` | Optional, nvarchar(max) | Task description can be lengthy; explicit `HasColumnType` ensures consistent mapping across providers. |
| `TaskOrder` | Required | Ordering position must always have a value. |
| `ColumnId` FK | Cascade delete | AC #26: Deleting a column removes all its tasks. |
| `AssignedId` FK | Optional, SetNull delete | AC #24 and #27: A task may have no assigned user; if the assigned user is deleted, the assignment is cleared (set to null) rather than deleting the task. |

**Delete Behavior — SetNull vs Restrict:**

The AC states: "If an assigned User is removed, the task is not removed; instead, the assignment is cleared or the deletion is restricted." `SetNull` is chosen over `Restrict` because:

1. **AC alignment:** "the assignment is cleared" is the primary described behavior — `SetNull` directly implements this by setting `AssignedId` to `NULL` when the referenced `User` is deleted.
2. **Optional FK:** `AssignedId` is already nullable, so `SetNull` is semantically natural — the column can hold `NULL` by design.
3. **No multiple cascade paths:** SQL Server's "multiple cascade paths" restriction applies when a single entity's deletion can reach the same target table through multiple FK cascade chains. Here, `User` reaches `KanbanTask` through only one direct FK (`AssignedId`). The `Project → BoardColumn → KanbanTask` cascade chain originates from `Project`, not `User`, so there is no conflict.
4. **UX:** A user deletion should not be blocked by task assignments — unassigning tasks is the expected behavior.

### 7. `ApplicationDbContext` Update

**File:** `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`

Add two new `DbSet<>` properties:

```csharp
public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();
public DbSet<KanbanTask> KanbanTasks => Set<KanbanTask>();
```

No changes to `OnModelCreating` — `ApplyConfigurationsFromAssembly` already auto-discovers all `IEntityTypeConfiguration<T>` implementations. No changes to `SaveChangesAsync` — timestamp stamping already covers all `BaseEntity` subclasses.

### 8. Migration

**Name:** `AddBoardColumnAndKanbanTaskEntities`

The migration will produce two tables:

**`BoardColumns` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `Name` | `nvarchar(100)` | NO | |
| `ColorCode` | `nvarchar(20)` | YES | |
| `ColumnOrder` | `int` | NO | |
| `ProjectId` | `uniqueidentifier` | NO | FK → Projects(Id), ON DELETE CASCADE |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**`KanbanTasks` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `Title` | `nvarchar(200)` | NO | |
| `Content` | `nvarchar(max)` | YES | |
| `TaskOrder` | `int` | NO | |
| `ColumnId` | `uniqueidentifier` | NO | FK → BoardColumns(Id), ON DELETE CASCADE |
| `AssignedId` | `uniqueidentifier` | YES | FK → Users(Id), ON DELETE SET NULL |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**Indexes (auto-created by EF Core for FK navigation):**
- `IX_BoardColumns_ProjectId` — Non-unique on `ProjectId`
- `IX_KanbanTasks_ColumnId` — Non-unique on `ColumnId`
- `IX_KanbanTasks_AssignedId` — Non-unique on `AssignedId`

## API Contracts

N/A — no API endpoints for this issue. The entities are domain models only at this stage. API endpoints for board, column, and task CRUD will be introduced in subsequent issues.

## Application Layer Boundaries

N/A — no service interfaces, MediatR handlers, or DTOs are required for this issue. Those will be introduced when API endpoints are added.

## Implementation Steps for @agent_developer

Follow these steps strictly in order, referencing `@rule_code_standards`:

1. **Create `BoardColumn` entity:**
   - Create `BoardColumn.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `Name` (string, default `string.Empty`), `ColorCode` (string?, no default), `ColumnOrder` (int).
   - Add `ProjectId` (Guid), `Project` (navigation, `= null!`).
   - Add `Tasks` (ICollection\<KanbanTask\>, default `new List<KanbanTask>()`).
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

2. **Create `KanbanTask` entity:**
   - Create `KanbanTask.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `Title` (string, default `string.Empty`), `Content` (string?, no default), `TaskOrder` (int).
   - Add `ColumnId` (Guid), `Column` (BoardColumn navigation, `= null!`).
   - Add `AssignedId` (Guid?), `AssignedUser` (User?, no `null!` — genuinely optional).
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

3. **Update `Project` entity — add navigation property:**
   - Open `KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs`.
   - Add `public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();` after the `Members` property.

4. **Update `User` entity — add navigation property:**
   - Open `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`.
   - Add `public ICollection<KanbanTask> AssignedTasks { get; set; } = new List<KanbanTask>();` after the `ProjectMemberships` property.

5. **Create `BoardColumnConfiguration`:**
   - Create `BoardColumnConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<BoardColumn>` with all constraints specified in Section 5 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

6. **Create `KanbanTaskConfiguration`:**
   - Create `KanbanTaskConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<KanbanTask>` with all constraints, relationships, and delete behaviors specified in Section 6 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

7. **Update `ApplicationDbContext`:**
   - Open `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`.
   - Add `public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();`.
   - Add `public DbSet<KanbanTask> KanbanTasks => Set<KanbanTask>();`.
   - Add necessary `using` statements if not already present (entities are in the same namespace import that already exists).

8. **Generate EF Core migration:**
   - From the `KanbAI-Core/KanbAI-Core` project directory, run:
     ```
     dotnet ef migrations add AddBoardColumnAndKanbanTaskEntities
     ```
   - If `dotnet ef` fails due to WDAC policy, a `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` — it should resolve the issue.
   - Verify the generated migration creates both tables with all expected columns, constraints, FKs, and indexes per the schema tables in Section 8 above.
   - Pay particular attention to the `AssignedId` FK: verify it uses `ON DELETE SET NULL` (not CASCADE or RESTRICT).

9. **Build and verify:**
   - Run `dotnet build` from the solution directory to ensure the solution compiles without errors.
   - Run `dotnet test` to confirm all existing tests continue to pass.

## QA Guidance for @agent_tester_qa

### Unit Tests Required

**File:** `KanbAI-Core.Tests/Models/Entities/BoardColumnTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `BoardColumn_InheritsFromBaseEntity` | Happy Path | Verify `typeof(BoardColumn).IsSubclassOf(typeof(BaseEntity))` |
| `BoardColumn_NameProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `BoardColumn_ColorCodeProperty_IsNullableStringType` | Happy Path | Verify property type is `string?` via reflection |
| `BoardColumn_ColumnOrderProperty_IsIntType` | Happy Path | Verify property type via reflection |
| `BoardColumn_ProjectIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `BoardColumn_ProjectNavigationProperty_Exists` | Happy Path | Verify `Project` navigation property exists and is `Project` type |
| `BoardColumn_TasksProperty_IsCollectionOfKanbanTask` | Happy Path | Verify the `Tasks` property is `ICollection<KanbanTask>` |
| `BoardColumn_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all properties are publicly accessible |
| `BoardColumn_DefaultPropertyValues_AreExpected` | Edge Case | New `BoardColumn()` should have `string.Empty` for Name, null for ColorCode, 0 for ColumnOrder, empty list for Tasks |

**File:** `KanbAI-Core.Tests/Models/Entities/KanbanTaskTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `KanbanTask_InheritsFromBaseEntity` | Happy Path | Verify `typeof(KanbanTask).IsSubclassOf(typeof(BaseEntity))` |
| `KanbanTask_TitleProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `KanbanTask_ContentProperty_IsNullableStringType` | Happy Path | Verify property type is `string?` via reflection |
| `KanbanTask_TaskOrderProperty_IsIntType` | Happy Path | Verify property type via reflection |
| `KanbanTask_ColumnIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `KanbanTask_ColumnNavigationProperty_Exists` | Happy Path | Verify `Column` navigation property exists and is `BoardColumn` type |
| `KanbanTask_AssignedIdProperty_IsNullableGuidType` | Happy Path | Verify property type is `Guid?` via reflection |
| `KanbanTask_AssignedUserNavigationProperty_Exists` | Happy Path | Verify `AssignedUser` navigation property exists and is `User?` type |
| `KanbanTask_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all properties are publicly accessible |
| `KanbanTask_DefaultPropertyValues_AreExpected` | Edge Case | New `KanbanTask()` should have `string.Empty` for Title, null for Content, 0 for TaskOrder, null for AssignedId, null for AssignedUser |

**File:** `KanbAI-Core.Tests/Data/Configurations/BoardColumnConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `BoardColumnConfiguration_Name_IsRequired` | Happy Path | A column with null/empty name should fail on `SaveChangesAsync` |
| `BoardColumnConfiguration_Name_MaxLength100_Accepted` | Happy Path | A column with 100-char name should save successfully |
| `BoardColumnConfiguration_ColorCode_IsOptional` | Happy Path | A column with null ColorCode should save successfully |
| `BoardColumnConfiguration_CascadeDelete_RemovesColumnsWhenProjectDeleted` | Happy Path | Deleting a project should cascade-delete its `BoardColumn` records |

**File:** `KanbAI-Core.Tests/Data/Configurations/KanbanTaskConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `KanbanTaskConfiguration_Title_IsRequired` | Happy Path | A task with null/empty title should fail on `SaveChangesAsync` |
| `KanbanTaskConfiguration_Title_MaxLength200_Accepted` | Happy Path | A task with 200-char title should save successfully |
| `KanbanTaskConfiguration_Content_IsOptional` | Happy Path | A task with null Content should save successfully |
| `KanbanTaskConfiguration_AssignedId_IsOptional` | Happy Path | A task with null AssignedId should save successfully |
| `KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenColumnDeleted` | Happy Path | Deleting a column should cascade-delete its `KanbanTask` records |
| `KanbanTaskConfiguration_SetNull_ClearsAssignmentWhenUserDeleted` | Happy Path | Deleting an assigned user should set `AssignedId` to null (not delete the task) |
| `KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenProjectDeleted` | Happy Path | Deleting a project should cascade through columns to delete all tasks |

**File:** `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ApplicationDbContext_BoardColumnsDbSet_IsNotNull` | Happy Path | Verify `context.BoardColumns` returns a non-null `DbSet<BoardColumn>` |
| `ApplicationDbContext_KanbanTasksDbSet_IsNotNull` | Happy Path | Verify `context.KanbanTasks` returns a non-null `DbSet<KanbanTask>` |

**File:** `KanbAI-Core.Tests/Models/Entities/ProjectTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `Project_ColumnsProperty_IsCollectionOfBoardColumn` | Happy Path | Verify the new `Columns` navigation property exists and is `ICollection<BoardColumn>` |

**File:** `KanbAI-Core.Tests/Models/Entities/UserTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `User_AssignedTasksProperty_IsCollectionOfKanbanTask` | Happy Path | Verify the new `AssignedTasks` navigation property exists and is `ICollection<KanbanTask>` |

### Test Infrastructure Notes

- Use `SQLite in-memory` for configuration tests that validate constraints (required fields, cascade behavior, SetNull behavior). The InMemory provider does not enforce these.
- Use `InMemoryDatabase` for simple entity/DbSet existence tests (consistent with existing patterns).
- Follow the `MethodName_StateUnderTest_ExpectedBehavior` naming convention per `@rule_testing_observability`.
- Follow the AAA pattern with blank-line separators per existing test conventions.
- No integration tests (`WebApplicationFactory`) are required for this issue as there are no API endpoints.
- **SetNull test note:** SQLite supports `ON DELETE SET NULL`, so the `SetNull_ClearsAssignmentWhenUserDeleted` test should work with SQLite in-memory. The test should: (1) create a user and a task assigned to that user, (2) delete the user, (3) verify the task still exists with `AssignedId == null`.

## Known Caveats

| Caveat | Impact | Mitigation |
|--------|--------|------------|
| **SQL Server SetNull with cascade paths** | SQL Server may reject `ON DELETE SET NULL` on `AssignedId` if it detects multiple cascade paths to `KanbanTask`. Analysis indicates no conflict (User reaches KanbanTask through only one direct FK), but this should be verified during migration application. | If SQL Server rejects SetNull, change `KanbanTaskConfiguration` to use `DeleteBehavior.Restrict` and update application logic to unassign tasks before user deletion. |
| **InMemory provider limitations** | InMemory does not enforce required fields, FK constraints, cascade behaviors, or SetNull behaviors. | Use SQLite in-memory for configuration tests that validate constraints. |
| **SQLite max-length limitations** | SQLite does not enforce `nvarchar(N)` max lengths. | Max-length validation (e.g., Name > 100 chars, Title > 200 chars) requires SQL Server integration tests, which are out of scope for this issue. |
| **SQLite SetNull behavior** | SQLite supports `ON DELETE SET NULL` but may differ from SQL Server in edge cases (e.g., FK enforcement must be enabled via `PRAGMA foreign_keys = ON`). | Ensure SQLite test setup enables FK enforcement. Existing SQLite test infrastructure should already handle this. |
| **No ordering uniqueness constraint** | `ColumnOrder` and `TaskOrder` have no unique index, so multiple items can temporarily share the same order value. | This is intentional — drag-and-drop reordering will produce temporary collisions that the application layer manages. |
| **WDAC policy** | `dotnet ef` may fail on host startup due to Windows WDAC policy blocking `Scalar.AspNetCore.dll`. | A `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` and should resolve this. |
| **GitHub comment schema discrepancy** | The issue comment's SQL schema uses `INT IDENTITY(1,1)` PKs and names the table `Tasks`. | The AC is authoritative. Guid PKs via `BaseEntity` are used, and the entity is named `KanbanTask` to avoid conflict with `System.Threading.Tasks.Task`. The DbSet is named `KanbanTasks`. |

## Development Status

**Status:** Complete — implemented by @agent_developer.

### Files Created

| File | Purpose |
|------|---------|
| `KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs` | `BoardColumn` entity inheriting `BaseEntity` with Name, ColorCode, ColumnOrder, ProjectId FK, and Tasks navigation |
| `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs` | `KanbanTask` entity inheriting `BaseEntity` with Title, Content, TaskOrder, ColumnId FK, optional AssignedId FK, and navigation properties |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/BoardColumnConfiguration.cs` | EF Core fluent configuration for `BoardColumn` — PK, Name required/MaxLength(100), ColorCode MaxLength(20), FK to Project with Cascade delete |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/KanbanTaskConfiguration.cs` | EF Core fluent configuration for `KanbanTask` — PK, Title required/MaxLength(200), FK to BoardColumn with Cascade delete, optional FK to User with SetNull delete |
| `KanbAI-Core/KanbAI-Core/Migrations/*_AddBoardColumnAndKanbanTaskEntities.cs` | EF Core migration creating BoardColumns and KanbanTasks tables |

### Files Modified

| File | Change |
|------|--------|
| `KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs` | Added `ICollection<BoardColumn> Columns` navigation property |
| `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs` | Added `ICollection<KanbanTask> AssignedTasks` navigation property |
| `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs` | Added `DbSet<BoardColumn> BoardColumns` and `DbSet<KanbanTask> KanbanTasks` |

### Build & Test Results

| Metric | Result |
|--------|--------|
| Build | SUCCESS — 0 errors, 0 warnings |
| Tests | 139 total, 137 passed, 0 failed, 2 skipped (pre-existing) |

### Infrastructure Notes

- **`HasColumnType("nvarchar(max)")` removed from `KanbanTaskConfiguration`:** The tech spec specified an explicit `.HasColumnType("nvarchar(max)")` for the `Content` property. This caused 13 pre-existing SQLite-based configuration tests to fail with `SQLite Error 1: 'near "max": syntax error'`. The explicit call was removed because EF Core already defaults `string?` without `HasMaxLength` to `nvarchar(max)` on SQL Server — the generated migration still produces the correct `nvarchar(max)` column type. This is consistent with the tech spec's own design note: "Mapped to `nvarchar(max)` by EF Core's default string mapping (no `HasMaxLength` applied)."

### Edge Cases for QA

- **SetNull delete behavior:** Verify that deleting a `User` who is assigned to a `KanbanTask` sets `AssignedId` to `NULL` rather than deleting the task or blocking the deletion. SQLite supports this but requires `PRAGMA foreign_keys = ON`.
- **Cascade chain:** Verify that deleting a `Project` cascades through `BoardColumn` to delete all associated `KanbanTask` records (three-level cascade).
- **No ordering uniqueness:** `ColumnOrder` and `TaskOrder` have no unique constraint — multiple items can share the same order value (intentional for drag-and-drop support).
- **Content column:** Verify `Content` is nullable and accepts large text values.
- **SQLite test compatibility:** The `Content` property configuration no longer uses explicit `HasColumnType("nvarchar(max)")`, so all SQLite-based tests should work. Max-length enforcement (e.g., Name > 100 chars) requires SQL Server integration tests (out of scope).

## Testing Status

**Status:** Complete — all tests pass.

### Test Files Created

| File | Tests | Category |
|------|-------|----------|
| `KanbAI-Core.Tests/Models/Entities/BoardColumnTests.cs` | 9 | Unit (entity) |
| `KanbAI-Core.Tests/Models/Entities/KanbanTaskTests.cs` | 10 | Unit (entity) |
| `KanbAI-Core.Tests/Data/Configurations/BoardColumnConfigurationTests.cs` | 4 | Configuration (SQLite) |
| `KanbAI-Core.Tests/Data/Configurations/KanbanTaskConfigurationTests.cs` | 7 | Configuration (SQLite) |

### Test Files Extended

| File | New Tests | Category |
|------|-----------|----------|
| `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` | 2 | Unit (DbSet) |
| `KanbAI-Core.Tests/Models/Entities/ProjectTests.cs` | 1 | Unit (entity) |
| `KanbAI-Core.Tests/Models/Entities/UserTests.cs` | 1 | Unit (entity) |

### Test Results

| Test Name | Status |
|-----------|--------|
| `BoardColumn_InheritsFromBaseEntity` | PASS |
| `BoardColumn_NameProperty_IsStringType` | PASS |
| `BoardColumn_ColorCodeProperty_IsNullableStringType` | PASS |
| `BoardColumn_ColumnOrderProperty_IsIntType` | PASS |
| `BoardColumn_ProjectIdProperty_IsGuidType` | PASS |
| `BoardColumn_ProjectNavigationProperty_Exists` | PASS |
| `BoardColumn_TasksProperty_IsCollectionOfKanbanTask` | PASS |
| `BoardColumn_AllProperties_HavePublicGettersAndSetters` | PASS |
| `BoardColumn_DefaultPropertyValues_AreExpected` | PASS |
| `KanbanTask_InheritsFromBaseEntity` | PASS |
| `KanbanTask_TitleProperty_IsStringType` | PASS |
| `KanbanTask_ContentProperty_IsNullableStringType` | PASS |
| `KanbanTask_TaskOrderProperty_IsIntType` | PASS |
| `KanbanTask_ColumnIdProperty_IsGuidType` | PASS |
| `KanbanTask_ColumnNavigationProperty_Exists` | PASS |
| `KanbanTask_AssignedIdProperty_IsNullableGuidType` | PASS |
| `KanbanTask_AssignedUserNavigationProperty_Exists` | PASS |
| `KanbanTask_AllProperties_HavePublicGettersAndSetters` | PASS |
| `KanbanTask_DefaultPropertyValues_AreExpected` | PASS |
| `BoardColumnConfiguration_Name_IsRequired` | PASS |
| `BoardColumnConfiguration_Name_MaxLength100_Accepted` | PASS |
| `BoardColumnConfiguration_ColorCode_IsOptional` | PASS |
| `BoardColumnConfiguration_CascadeDelete_RemovesColumnsWhenProjectDeleted` | PASS |
| `KanbanTaskConfiguration_Title_IsRequired` | PASS |
| `KanbanTaskConfiguration_Title_MaxLength200_Accepted` | PASS |
| `KanbanTaskConfiguration_Content_IsOptional` | PASS |
| `KanbanTaskConfiguration_AssignedId_IsOptional` | PASS |
| `KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenColumnDeleted` | PASS |
| `KanbanTaskConfiguration_SetNull_ClearsAssignmentWhenUserDeleted` | PASS |
| `KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenProjectDeleted` | PASS |
| `ApplicationDbContext_BoardColumnsDbSet_IsNotNull` | PASS |
| `ApplicationDbContext_KanbanTasksDbSet_IsNotNull` | PASS |
| `Project_ColumnsProperty_IsCollectionOfBoardColumn` | PASS |
| `User_AssignedTasksProperty_IsCollectionOfKanbanTask` | PASS |

### Summary

| Metric | Result |
|--------|--------|
| New tests written | 34 |
| Total suite | 173 total, 171 passed, 0 failed, 2 skipped (pre-existing) |
| Coverage gaps | Max-length enforcement (Name > 100, Title > 200) requires SQL Server integration tests (out of scope for this domain-model issue) |
