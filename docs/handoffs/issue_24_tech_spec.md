# Technical Specification: Issue #24 - Implement Attachments (Assets) and Comments Domains

## Overview

Create the `Asset` and `TaskComment` domain entities to support file attachments and threaded discussions on Kanban tasks. An `Asset` stores metadata about an uploaded file (storage key, MIME type, file size, thumbnail key, processing status) attached to a `KanbanTask`. A `TaskComment` represents a text comment authored by a `User` on a `KanbanTask`. Both entities inherit from `BaseEntity` and follow the patterns established by `BoardColumn` and `KanbanTask` in issue #23. A new `ProcessingStatus` enum tracks the lifecycle state of uploaded files. No API endpoints or application-layer services are required at this stage — this is a domain-model-only issue that completes the core entity graph before `AppDbContext` finalization in issue #25.

---

## Database/Domain Design

### 1. `ProcessingStatus` Enum

**File:** `KanbAI-Core/KanbAI-Core/Models/Enums/ProcessingStatus.cs`
**Namespace:** `KanbAI_Core.Models.Enums`

```csharp
namespace KanbAI_Core.Models.Enums;

public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
```

**Design Notes:**
- `Pending = 0` is the default. A newly uploaded file has not yet been processed (thumbnail generation, virus scan, etc.).
- `Processing = 1` indicates the file is actively being processed by a background job.
- `Completed = 2` indicates processing finished successfully.
- `Failed = 3` indicates processing encountered an error.
- Explicit integer values follow the convention established by `UserRole` and `ProjectRole`.

### 2. `Asset` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Models.Entities;

public class Asset : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? ThumbnailKey { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }

    public Guid KanbanTaskId { get; set; }
    public KanbanTask KanbanTask { get; set; } = null!;
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- `FileName` uses `string.Empty` default per project convention; the database requires a non-null, non-empty value via EF configuration. Represents the original file name as provided by the uploader.
- `StorageKey` uses `string.Empty` default; required and unique — no two assets may share the same storage location in external storage. MaxLength(1024) accommodates S3-style key paths.
- `ThumbnailKey` is nullable (`string?`) because not all file types produce thumbnails (e.g., plain text files). Same MaxLength(1024) as `StorageKey`.
- `MimeType` uses `string.Empty` default; required. MaxLength(256) accommodates all IANA media types per RFC 6838.
- `FileSize` is `long` (not `int`) because files can exceed 2 GB. A check constraint enforces non-negative values at the database level.
- `ProcessingStatus` defaults to `Pending` (0) via EF configuration. Required — every asset must have a known state.
- `KanbanTaskId` is a required `Guid` FK. An asset cannot exist without a parent task (AC #22).
- `KanbanTask` navigation uses `null!` per the EF Core convention established in `BoardColumn` and `KanbanTask` for required navigation properties.
- The `BaseEntity.CreatedAt` property serves the same purpose as the `UploadedAt` column mentioned in the issue comment's SQL sketch — no separate property is needed.

### 3. `TaskComment` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/TaskComment.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
namespace KanbAI_Core.Models.Entities;

public class TaskComment : BaseEntity
{
    public string Content { get; set; } = string.Empty;

    public Guid KanbanTaskId { get; set; }
    public KanbanTask KanbanTask { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- `Content` uses `string.Empty` default per project convention; required at the database level. Maps to `nvarchar(max)` by EF Core's default string mapping (no `HasMaxLength` applied) since comments can be arbitrarily long.
- `KanbanTaskId` is a required `Guid` FK. A comment cannot exist without a parent task (AC #23).
- `AuthorId` is a required `Guid` FK. A comment cannot exist without an author (AC #24).
- Both navigation properties use `null!` per the established convention for required navigation properties.
- The whitespace-only check (AC #28: "Content cannot be empty or whitespace-only") cannot be enforced by a simple `NOT NULL` SQL constraint. `.IsRequired()` prevents NULL and empty strings at the EF Core level. Whitespace-only validation should be enforced at the application/DTO validation layer in a future issue.

### 4. `KanbanTask` Entity Update — Navigation Properties

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

Add the following navigation properties to satisfy AC #16 and #17:

```csharp
public ICollection<Asset> Assets { get; set; } = new List<Asset>();
public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
```

**Updated `KanbanTask` class:**

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

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
}
```

### 5. `User` Entity Update — Navigation Property

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

Add the following navigation property to satisfy AC #18:

```csharp
public ICollection<TaskComment> AuthoredComments { get; set; } = new List<TaskComment>();
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
    public ICollection<TaskComment> AuthoredComments { get; set; } = new List<TaskComment>();
}
```

### 6. EF Core Entity Configuration — `AssetConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/AssetConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.StorageKey)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(a => a.StorageKey)
            .IsUnique();

        builder.Property(a => a.ThumbnailKey)
            .HasMaxLength(1024);

        builder.Property(a => a.MimeType)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.FileSize)
            .IsRequired();

        builder.Property(a => a.ProcessingStatus)
            .IsRequired()
            .HasDefaultValue(ProcessingStatus.Pending);

        builder.HasOne(a => a.KanbanTask)
            .WithMany(kt => kt.Assets)
            .HasForeignKey(a => a.KanbanTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Constraint Rationale:**

| Property / Constraint | Type | Reason |
|-----------------------|------|--------|
| `Id` | Primary Key | Surrogate PK inherited from `BaseEntity`. |
| `FileName` | Required, MaxLength(255) | Original file name cannot be blank (AC #31); 255 chars is the standard OS file name limit. |
| `StorageKey` | Required, MaxLength(1024), Unique Index | Unique storage location key (AC #29); 1024 chars accommodates S3-style key paths. |
| `ThumbnailKey` | Optional, MaxLength(1024) | Nullable — not all file types produce thumbnails (AC #33). Same max length as `StorageKey`. |
| `MimeType` | Required, MaxLength(256) | Content type cannot be blank; 256 chars accommodates all IANA media types per RFC 6838. |
| `FileSize` | Required | File size in bytes. Non-negative constraint is documented as application-layer validation (see Known Caveats). |
| `ProcessingStatus` | Required, Default `Pending` | Every asset must have a known state (AC #32). New uploads default to `Pending`. |
| `KanbanTaskId` FK | Cascade delete | AC #25: Deleting a task removes all its assets. |

**Note:** The `KanbanTask → Assets` relationship is configured from the `AssetConfiguration` side (the dependent entity owns the FK), consistent with how `KanbanTaskConfiguration` configures the `BoardColumn → Tasks` relationship.

### 7. EF Core Entity Configuration — `TaskCommentConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/TaskCommentConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.HasKey(tc => tc.Id);

        builder.Property(tc => tc.Content)
            .IsRequired();

        builder.HasOne(tc => tc.KanbanTask)
            .WithMany(kt => kt.Comments)
            .HasForeignKey(tc => tc.KanbanTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tc => tc.Author)
            .WithMany(u => u.AuthoredComments)
            .HasForeignKey(tc => tc.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

**Constraint Rationale:**

| Property / Constraint | Type | Reason |
|-----------------------|------|--------|
| `Id` | Primary Key | Surrogate PK inherited from `BaseEntity`. |
| `Content` | Required | Comment body cannot be NULL or empty (AC #28). Mapped to `nvarchar(max)` by default — comments can be long. |
| `KanbanTaskId` FK | Cascade delete | AC #26: Deleting a task removes all its comments. |
| `AuthorId` FK | Restrict delete | AC #27: Deleting a user who has authored comments is blocked to prevent orphaned content. |

**Delete Behavior — Restrict for User → TaskComment:**

The AC states: "If a User is removed, authored comments are not silently deleted; the deletion is restricted to prevent orphaned content." `Restrict` is chosen because:

1. **AC alignment:** "the deletion is restricted" directly maps to `DeleteBehavior.Restrict`.
2. **Data integrity:** Comments are a permanent record of discussion. Silently deleting them on user removal would violate audit and collaboration expectations.
3. **Required FK:** `AuthorId` is non-nullable — `SetNull` is not an option since every comment must have an author.
4. **No multiple cascade paths:** SQL Server's "multiple cascade paths" restriction is not triggered. From `User`, the path to `TaskComment` via `AuthorId` is `Restrict` (not a cascading action). The indirect path `User → KanbanTask (SetNull on AssignedId) → TaskComment (Cascade)` does not produce a cascade to `TaskComment` because `SetNull` modifies the `AssignedId` column on `KanbanTask` without deleting `KanbanTask` rows. Only one actual cascading referential action path reaches `TaskComment`: `Project → BoardColumn → KanbanTask → TaskComment`.

### 8. `ApplicationDbContext` Update

**File:** `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`

Add two new `DbSet<>` properties:

```csharp
public DbSet<Asset> Assets => Set<Asset>();
public DbSet<TaskComment> TaskComments => Set<TaskComment>();
```

No changes to `OnModelCreating` — `ApplyConfigurationsFromAssembly` already auto-discovers all `IEntityTypeConfiguration<T>` implementations. No changes to `SaveChangesAsync` — timestamp stamping already covers all `BaseEntity` subclasses.

### 9. Migration

**Name:** `AddAssetAndTaskCommentEntities`

The migration will produce two tables:

**`Assets` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `FileName` | `nvarchar(255)` | NO | |
| `StorageKey` | `nvarchar(1024)` | NO | Unique Index (`IX_Assets_StorageKey`) |
| `ThumbnailKey` | `nvarchar(1024)` | YES | |
| `MimeType` | `nvarchar(256)` | NO | |
| `FileSize` | `bigint` | NO | |
| `ProcessingStatus` | `int` | NO | Default: 0 (Pending) |
| `KanbanTaskId` | `uniqueidentifier` | NO | FK → KanbanTasks(Id), ON DELETE CASCADE |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**`TaskComments` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `Content` | `nvarchar(max)` | NO | |
| `KanbanTaskId` | `uniqueidentifier` | NO | FK → KanbanTasks(Id), ON DELETE CASCADE |
| `AuthorId` | `uniqueidentifier` | NO | FK → Users(Id), ON DELETE NO ACTION |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**Indexes (auto-created by EF Core for FK navigation):**
- `IX_Assets_StorageKey` — Unique on `StorageKey`
- `IX_Assets_KanbanTaskId` — Non-unique on `KanbanTaskId`
- `IX_TaskComments_KanbanTaskId` — Non-unique on `KanbanTaskId`
- `IX_TaskComments_AuthorId` — Non-unique on `AuthorId`

**Note on Restrict vs NoAction:** EF Core's `DeleteBehavior.Restrict` generates `ON DELETE NO ACTION` in the SQL migration for SQL Server. Both prevent cascading deletes; `NO ACTION` is the SQL Server convention that Restrict maps to.

---

## API Contracts

N/A — no API endpoints for this issue. The entities are domain models only at this stage. API endpoints for asset upload/download and comment CRUD will be introduced in subsequent issues.

---

## Application Layer Boundaries

N/A — no service interfaces, MediatR handlers, or DTOs are required for this issue. Those will be introduced when API endpoints are added.

---

## Implementation Steps for @agent_developer

Follow these steps strictly in order, referencing `@rule_code_standards`:

1. **Create `ProcessingStatus` enum:**
   - Create `ProcessingStatus.cs` in `KanbAI-Core/KanbAI-Core/Models/Enums/`.
   - Define values: `Pending = 0`, `Processing = 1`, `Completed = 2`, `Failed = 3`.
   - Use file-scoped namespace `KanbAI_Core.Models.Enums`.

2. **Create `Asset` entity:**
   - Create `Asset.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `FileName` (string, default `string.Empty`), `StorageKey` (string, default `string.Empty`), `ThumbnailKey` (string?, no default), `MimeType` (string, default `string.Empty`), `FileSize` (long), `ProcessingStatus` (ProcessingStatus enum).
   - Add `KanbanTaskId` (Guid), `KanbanTask` (navigation, `= null!`).
   - Add `using KanbAI_Core.Models.Enums;` import for `ProcessingStatus`.
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

3. **Create `TaskComment` entity:**
   - Create `TaskComment.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `Content` (string, default `string.Empty`).
   - Add `KanbanTaskId` (Guid), `KanbanTask` (navigation, `= null!`).
   - Add `AuthorId` (Guid), `Author` (User navigation, `= null!`).
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

4. **Update `KanbanTask` entity — add navigation properties:**
   - Open `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs`.
   - Add `public ICollection<Asset> Assets { get; set; } = new List<Asset>();` after the `AssignedUser` property.
   - Add `public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();` after the `Assets` property.

5. **Update `User` entity — add navigation property:**
   - Open `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`.
   - Add `public ICollection<TaskComment> AuthoredComments { get; set; } = new List<TaskComment>();` after the `AssignedTasks` property.

6. **Create `AssetConfiguration`:**
   - Create `AssetConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<Asset>` with all constraints specified in Section 6 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

7. **Create `TaskCommentConfiguration`:**
   - Create `TaskCommentConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<TaskComment>` with all constraints and relationships specified in Section 7 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

8. **Update `ApplicationDbContext`:**
   - Open `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`.
   - Add `public DbSet<Asset> Assets => Set<Asset>();`.
   - Add `public DbSet<TaskComment> TaskComments => Set<TaskComment>();`.
   - Add necessary `using` statements if not already present (entities are in the same namespace import that already exists).

9. **Generate EF Core migration:**
   - From the `KanbAI-Core/KanbAI-Core` project directory, run:
     ```
     dotnet ef migrations add AddAssetAndTaskCommentEntities
     ```
   - If `dotnet ef` fails due to WDAC policy, a `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` — it should resolve the issue.
   - Verify the generated migration creates both tables with all expected columns, constraints, FKs, and indexes per the schema tables in Section 9 above.
   - Pay particular attention to:
     - `StorageKey` unique index (`IX_Assets_StorageKey`).
     - `ProcessingStatus` default value (0 / Pending).
     - `AuthorId` FK uses `ON DELETE NO ACTION` (Restrict behavior).

10. **Build and verify:**
    - Run `dotnet build` from the solution directory to ensure the solution compiles without errors.
    - Run `dotnet test` to confirm all existing tests continue to pass.

---

## QA Guidance for @agent_tester_qa

### Unit Tests Required

**File:** `KanbAI-Core.Tests/Models/Entities/AssetTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `Asset_InheritsFromBaseEntity` | Happy Path | Verify `typeof(Asset).IsSubclassOf(typeof(BaseEntity))` |
| `Asset_FileNameProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `Asset_StorageKeyProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `Asset_ThumbnailKeyProperty_IsNullableStringType` | Happy Path | Verify property type is `string?` via reflection |
| `Asset_MimeTypeProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `Asset_FileSizeProperty_IsLongType` | Happy Path | Verify property type is `long` via reflection |
| `Asset_ProcessingStatusProperty_IsProcessingStatusType` | Happy Path | Verify property type is `ProcessingStatus` via reflection |
| `Asset_KanbanTaskIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `Asset_KanbanTaskNavigationProperty_Exists` | Happy Path | Verify `KanbanTask` navigation property exists and is `KanbanTask` type |
| `Asset_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all properties are publicly accessible |
| `Asset_DefaultPropertyValues_AreExpected` | Edge Case | New `Asset()` should have `string.Empty` for FileName/StorageKey/MimeType, null for ThumbnailKey, 0 for FileSize, `ProcessingStatus.Pending` for ProcessingStatus |

**File:** `KanbAI-Core.Tests/Models/Entities/TaskCommentTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `TaskComment_InheritsFromBaseEntity` | Happy Path | Verify `typeof(TaskComment).IsSubclassOf(typeof(BaseEntity))` |
| `TaskComment_ContentProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `TaskComment_KanbanTaskIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `TaskComment_KanbanTaskNavigationProperty_Exists` | Happy Path | Verify `KanbanTask` navigation property exists and is `KanbanTask` type |
| `TaskComment_AuthorIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `TaskComment_AuthorNavigationProperty_Exists` | Happy Path | Verify `Author` navigation property exists and is `User` type |
| `TaskComment_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all properties are publicly accessible |
| `TaskComment_DefaultPropertyValues_AreExpected` | Edge Case | New `TaskComment()` should have `string.Empty` for Content, `Guid.Empty` for KanbanTaskId and AuthorId |

**File:** `KanbAI-Core.Tests/Models/Enums/ProcessingStatusTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ProcessingStatus_HasExpectedValues` | Happy Path | Verify all four values exist: Pending(0), Processing(1), Completed(2), Failed(3) |
| `ProcessingStatus_DefaultValue_IsPending` | Edge Case | Verify `default(ProcessingStatus)` equals `ProcessingStatus.Pending` (0) |

**File:** `KanbAI-Core.Tests/Data/Configurations/AssetConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `AssetConfiguration_FileName_IsRequired` | Happy Path | An asset with null/empty FileName should fail on `SaveChangesAsync` |
| `AssetConfiguration_FileName_MaxLength255_Accepted` | Happy Path | An asset with 255-char FileName should save successfully |
| `AssetConfiguration_StorageKey_IsRequired` | Happy Path | An asset with null/empty StorageKey should fail on `SaveChangesAsync` |
| `AssetConfiguration_StorageKey_IsUnique` | Edge Case | Two assets with the same StorageKey should fail on `SaveChangesAsync` |
| `AssetConfiguration_ThumbnailKey_IsOptional` | Happy Path | An asset with null ThumbnailKey should save successfully |
| `AssetConfiguration_MimeType_IsRequired` | Happy Path | An asset with null/empty MimeType should fail on `SaveChangesAsync` |
| `AssetConfiguration_CascadeDelete_RemovesAssetsWhenTaskDeleted` | Happy Path | Deleting a KanbanTask should cascade-delete its Asset records |
| `AssetConfiguration_ProcessingStatus_DefaultValue_IsPending` | Happy Path | An asset saved without explicitly setting ProcessingStatus should have `Pending` |

**File:** `KanbAI-Core.Tests/Data/Configurations/TaskCommentConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `TaskCommentConfiguration_Content_IsRequired` | Happy Path | A comment with null/empty Content should fail on `SaveChangesAsync` |
| `TaskCommentConfiguration_CascadeDelete_RemovesCommentsWhenTaskDeleted` | Happy Path | Deleting a KanbanTask should cascade-delete its TaskComment records |
| `TaskCommentConfiguration_RestrictDelete_BlocksUserDeletionWithComments` | Happy Path | Deleting a User who has authored comments should throw (Restrict behavior) |
| `TaskCommentConfiguration_AuthorId_IsRequired` | Happy Path | A comment without an AuthorId should fail on `SaveChangesAsync` |

**File:** `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ApplicationDbContext_AssetsDbSet_IsNotNull` | Happy Path | Verify `context.Assets` returns a non-null `DbSet<Asset>` |
| `ApplicationDbContext_TaskCommentsDbSet_IsNotNull` | Happy Path | Verify `context.TaskComments` returns a non-null `DbSet<TaskComment>` |

**File:** `KanbAI-Core.Tests/Models/Entities/KanbanTaskTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `KanbanTask_AssetsProperty_IsCollectionOfAsset` | Happy Path | Verify the new `Assets` navigation property exists and is `ICollection<Asset>` |
| `KanbanTask_CommentsProperty_IsCollectionOfTaskComment` | Happy Path | Verify the new `Comments` navigation property exists and is `ICollection<TaskComment>` |

**File:** `KanbAI-Core.Tests/Models/Entities/UserTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `User_AuthoredCommentsProperty_IsCollectionOfTaskComment` | Happy Path | Verify the new `AuthoredComments` navigation property exists and is `ICollection<TaskComment>` |

### Test Infrastructure Notes

- Use `SQLite in-memory` for configuration tests that validate constraints (required fields, cascade behavior, Restrict behavior, unique indexes). The InMemory provider does not enforce these.
- Use `InMemoryDatabase` for simple entity/DbSet existence tests (consistent with existing patterns).
- Follow the `MethodName_StateUnderTest_ExpectedBehavior` naming convention per `@rule_testing_observability`.
- Follow the AAA pattern with blank-line separators per existing test conventions.
- No integration tests (`WebApplicationFactory`) are required for this issue as there are no API endpoints.
- **Restrict delete test note:** SQLite maps `DeleteBehavior.Restrict` to `ON DELETE NO ACTION`. The test should: (1) create a user and a comment authored by that user, (2) attempt to delete the user, (3) verify that a `DbUpdateException` is thrown.
- **Unique index test note:** SQLite enforces unique indexes. The test should: (1) save an asset with a StorageKey, (2) attempt to save a second asset with the same StorageKey, (3) verify that a `DbUpdateException` is thrown.
- **ProcessingStatus default test note:** The test should save an `Asset` entity without explicitly setting `ProcessingStatus`, reload it from the database, and verify the value is `ProcessingStatus.Pending`.

---

## Known Caveats

| Caveat | Impact | Mitigation |
|--------|--------|------------|
| **FileSize non-negative constraint** | The AC requires `FileSize` to be non-negative (AC #30). EF Core's fluent API does not have a built-in non-negative constraint. A `HasCheckConstraint("CK_Assets_FileSize_NonNegative", "[FileSize] >= 0")` could be added but would break SQLite tests since SQLite check constraint syntax differs. | Enforce non-negative `FileSize` via application-layer validation (DTO validation / FluentValidation) in a future issue when the upload API is built. The `long` type and `IsRequired()` prevent nulls; negative values should be caught by business logic. |
| **Content whitespace-only constraint** | AC #28 requires `Content` cannot be whitespace-only. `.IsRequired()` prevents NULL and empty strings but not whitespace-only strings. A SQL check constraint (`LTRIM(RTRIM([Content])) <> ''`) would break SQLite tests. | Enforce whitespace validation at the application/DTO layer in a future issue when the comment API is built. |
| **InMemory provider limitations** | InMemory does not enforce required fields, FK constraints, cascade behaviors, Restrict behaviors, or unique indexes. | Use SQLite in-memory for configuration tests that validate constraints. |
| **SQLite max-length limitations** | SQLite does not enforce `nvarchar(N)` max lengths. | Max-length validation (e.g., FileName > 255 chars, StorageKey > 1024 chars) requires SQL Server integration tests, which are out of scope for this issue. |
| **SQL Server multiple cascade paths** | SQL Server may reject certain FK configurations when it detects multiple cascade paths. Analysis confirms no conflict: the `User → TaskComment` path uses `Restrict` (not a cascading action), so only one cascade path (`Project → BoardColumn → KanbanTask → TaskComment`) reaches `TaskComment`. If SQL Server rejects the migration, switch `TaskComment.KanbanTaskId` to `DeleteBehavior.Restrict` and handle cascade deletion in application logic. | Verified during design — no conflict expected. Monitor during migration application. |
| **WDAC policy** | `dotnet ef` may fail on host startup due to Windows WDAC policy blocking `Scalar.AspNetCore.dll`. | A `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` and should resolve this. |
| **GitHub comment schema discrepancy** | The issue comment's SQL schema uses `INT IDENTITY(1,1)` PKs, `TaskId` (not `KanbanTaskId`), `OriginalFileName` (not `FileName`), and `UploadedAt` (not `CreatedAt`). | The AC and context note are authoritative. Guid PKs via `BaseEntity` are used. `KanbanTaskId` matches the entity name to avoid confusion with `System.Threading.Tasks.Task`. `FileName` is simpler and sufficiently descriptive. `CreatedAt` from `BaseEntity` serves the same purpose as `UploadedAt`. |

---

## Development Status

### Files Created

| File Path | Purpose |
|-----------|---------|
| `KanbAI-Core/KanbAI-Core/Models/Enums/ProcessingStatus.cs` | `ProcessingStatus` enum with Pending(0), Processing(1), Completed(2), Failed(3) |
| `KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs` | `Asset` entity inheriting `BaseEntity` with file metadata properties and FK to `KanbanTask` |
| `KanbAI-Core/KanbAI-Core/Models/Entities/TaskComment.cs` | `TaskComment` entity inheriting `BaseEntity` with Content, FK to `KanbanTask`, FK to `User` |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/AssetConfiguration.cs` | EF Core fluent config for `Asset` (required fields, max lengths, unique StorageKey index, default ProcessingStatus, cascade delete) |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/TaskCommentConfiguration.cs` | EF Core fluent config for `TaskComment` (required Content, cascade delete from KanbanTask, restrict delete from User) |
| `KanbAI-Core/KanbAI-Core/Migrations/20260411130153_AddAssetAndTaskCommentEntities.cs` | Migration creating Assets and TaskComments tables |
| `KanbAI-Core/KanbAI-Core/Migrations/20260411130153_AddAssetAndTaskCommentEntities.Designer.cs` | Migration designer file (auto-generated) |

### Files Modified

| File Path | Change |
|-----------|--------|
| `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs` | Added `Assets` (`ICollection<Asset>`) and `Comments` (`ICollection<TaskComment>`) navigation properties |
| `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs` | Added `AuthoredComments` (`ICollection<TaskComment>`) navigation property |
| `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs` | Added `DbSet<Asset> Assets` and `DbSet<TaskComment> TaskComments` properties |
| `KanbAI-Core/KanbAI-Core/Migrations/ApplicationDbContextModelSnapshot.cs` | Updated model snapshot with Asset and TaskComment entity definitions (auto-generated) |

### Build & Test Results

| Metric | Result |
|--------|--------|
| **Build** | SUCCESS — 0 errors, 0 warnings |
| **Tests Total** | 173 |
| **Tests Passed** | 171 |
| **Tests Failed** | 0 |
| **Tests Skipped** | 2 (pre-existing) |

### Infrastructure Notes

No infrastructure workarounds were required. The existing `DesignTimeDbContextFactory` resolved `dotnet ef` design-time resolution.

### Edge Cases for QA

- **StorageKey uniqueness:** The unique index on `StorageKey` should be tested with SQLite in-memory (SQLite enforces unique indexes). Attempt to save two assets with the same StorageKey and verify `DbUpdateException`.
- **ProcessingStatus default:** Save an `Asset` without explicitly setting `ProcessingStatus`, reload from DB, and verify it equals `ProcessingStatus.Pending`.
- **Restrict delete on User → TaskComment:** Attempt to delete a `User` who has authored comments and verify `DbUpdateException` is thrown (SQLite maps `Restrict` to `ON DELETE NO ACTION`).
- **Cascade delete KanbanTask → Assets/Comments:** Delete a `KanbanTask` and verify all related `Asset` and `TaskComment` rows are removed.
- **Content required:** Attempt to save a `TaskComment` with null/empty Content and verify it fails.
- **FileSize non-negative:** Not enforced at the DB level (deferred to application-layer validation). QA should note this as a known caveat.
- **Content whitespace-only:** Not enforced at the DB level (deferred to application-layer validation). QA should note this as a known caveat.
- **Max-length enforcement:** SQLite does not enforce `nvarchar(N)` max lengths. Max-length validation requires SQL Server integration tests (out of scope).

---

## Testing Status

### Test Run Summary

| Metric | Result |
|--------|--------|
| **Total Tests** | 211 |
| **Passed** | 209 |
| **Failed** | 0 |
| **Skipped** | 2 (pre-existing) |
| **New Tests Added** | 38 |

### Test Files Created

| File | Test Count | Status |
|------|-----------|--------|
| `KanbAI-Core.Tests/Models/Enums/ProcessingStatusTests.cs` | 2 | All Passed |
| `KanbAI-Core.Tests/Models/Entities/AssetTests.cs` | 11 | All Passed |
| `KanbAI-Core.Tests/Models/Entities/TaskCommentTests.cs` | 8 | All Passed |
| `KanbAI-Core.Tests/Data/Configurations/AssetConfigurationTests.cs` | 8 | All Passed |
| `KanbAI-Core.Tests/Data/Configurations/TaskCommentConfigurationTests.cs` | 4 | All Passed |

### Test Files Extended

| File | Tests Added | Status |
|------|------------|--------|
| `KanbAI-Core.Tests/Models/Entities/KanbanTaskTests.cs` | 2 (`Assets`, `Comments` navigation properties) | All Passed |
| `KanbAI-Core.Tests/Models/Entities/UserTests.cs` | 1 (`AuthoredComments` navigation property) | All Passed |
| `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` | 2 (`Assets`, `TaskComments` DbSet) | All Passed |

### Test Details

**ProcessingStatusTests (2 tests)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `ProcessingStatus_HasExpectedValues` | Happy Path | PASS |
| `ProcessingStatus_DefaultValue_IsPending` | Edge Case | PASS |

**AssetTests (11 tests)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `Asset_InheritsFromBaseEntity` | Happy Path | PASS |
| `Asset_FileNameProperty_IsStringType` | Happy Path | PASS |
| `Asset_StorageKeyProperty_IsStringType` | Happy Path | PASS |
| `Asset_ThumbnailKeyProperty_IsNullableStringType` | Happy Path | PASS |
| `Asset_MimeTypeProperty_IsStringType` | Happy Path | PASS |
| `Asset_FileSizeProperty_IsLongType` | Happy Path | PASS |
| `Asset_ProcessingStatusProperty_IsProcessingStatusType` | Happy Path | PASS |
| `Asset_KanbanTaskIdProperty_IsGuidType` | Happy Path | PASS |
| `Asset_KanbanTaskNavigationProperty_Exists` | Happy Path | PASS |
| `Asset_AllProperties_HavePublicGettersAndSetters` | Happy Path | PASS |
| `Asset_DefaultPropertyValues_AreExpected` | Edge Case | PASS |

**TaskCommentTests (8 tests)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `TaskComment_InheritsFromBaseEntity` | Happy Path | PASS |
| `TaskComment_ContentProperty_IsStringType` | Happy Path | PASS |
| `TaskComment_KanbanTaskIdProperty_IsGuidType` | Happy Path | PASS |
| `TaskComment_KanbanTaskNavigationProperty_Exists` | Happy Path | PASS |
| `TaskComment_AuthorIdProperty_IsGuidType` | Happy Path | PASS |
| `TaskComment_AuthorNavigationProperty_Exists` | Happy Path | PASS |
| `TaskComment_AllProperties_HavePublicGettersAndSetters` | Happy Path | PASS |
| `TaskComment_DefaultPropertyValues_AreExpected` | Edge Case | PASS |

**AssetConfigurationTests (8 tests — SQLite in-memory)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `AssetConfiguration_FileName_IsRequired` | Happy Path | PASS |
| `AssetConfiguration_FileName_MaxLength255_Accepted` | Happy Path | PASS |
| `AssetConfiguration_StorageKey_IsRequired` | Happy Path | PASS |
| `AssetConfiguration_StorageKey_IsUnique` | Edge Case | PASS |
| `AssetConfiguration_ThumbnailKey_IsOptional` | Happy Path | PASS |
| `AssetConfiguration_MimeType_IsRequired` | Happy Path | PASS |
| `AssetConfiguration_CascadeDelete_RemovesAssetsWhenTaskDeleted` | Happy Path | PASS |
| `AssetConfiguration_ProcessingStatus_DefaultValue_IsPending` | Happy Path | PASS |

**TaskCommentConfigurationTests (4 tests — SQLite in-memory)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `TaskCommentConfiguration_Content_IsRequired` | Happy Path | PASS |
| `TaskCommentConfiguration_CascadeDelete_RemovesCommentsWhenTaskDeleted` | Happy Path | PASS |
| `TaskCommentConfiguration_RestrictDelete_BlocksUserDeletionWithComments` | Happy Path | PASS |
| `TaskCommentConfiguration_AuthorId_IsRequired` | Happy Path | PASS |

**Extended KanbanTaskTests (+2 tests)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `KanbanTask_AssetsProperty_IsCollectionOfAsset` | Happy Path | PASS |
| `KanbanTask_CommentsProperty_IsCollectionOfTaskComment` | Happy Path | PASS |

**Extended UserTests (+1 test)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `User_AuthoredCommentsProperty_IsCollectionOfTaskComment` | Happy Path | PASS |

**Extended ApplicationDbContextTests (+2 tests)**

| Test Name | Category | Result |
|-----------|----------|--------|
| `ApplicationDbContext_AssetsDbSet_IsNotNull` | Happy Path | PASS |
| `ApplicationDbContext_TaskCommentsDbSet_IsNotNull` | Happy Path | PASS |

### Known Coverage Gaps

| Gap | Reason |
|-----|--------|
| `FileSize` non-negative constraint | Not enforced at DB level — deferred to application-layer validation in a future issue |
| `Content` whitespace-only constraint | Not enforced at DB level — deferred to application-layer validation in a future issue |
| `FileName` max-length overflow (>255 chars) | SQLite does not enforce `nvarchar(N)` max lengths; requires SQL Server integration test |
| `StorageKey` max-length overflow (>1024 chars) | SQLite does not enforce `nvarchar(N)` max lengths; requires SQL Server integration test |
| `MimeType` max-length overflow (>256 chars) | SQLite does not enforce `nvarchar(N)` max lengths; requires SQL Server integration test |
