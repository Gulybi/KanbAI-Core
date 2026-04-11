# Technical Specification: Issue #22 - Implement Project Domain and N:M Relationship

## Overview

Create the `Project` domain entity and the `ProjectMember` junction entity to model the many-to-many relationship between Users and Projects. Each membership carries a `ProjectRole` to distinguish owners from regular members. This is the foundational domain structure for collaborative workspaces — all downstream entities (boards, columns, tasks, comments) will belong to a Project. No API endpoints or application-layer services are required at this stage.

## Database/Domain Design

### 1. `ProjectRole` Enum

**File:** `KanbAI-Core/KanbAI-Core/Models/Enums/ProjectRole.cs`
**Namespace:** `KanbAI_Core.Models.Enums`

```csharp
namespace KanbAI_Core.Models.Enums;

public enum ProjectRole
{
    Member = 0,
    Owner = 1
}
```

**Rationale:** Mirrors the `UserRole` pattern. `Member = 0` is the default (least privilege), so any uninitialized `ProjectRole` field defaults to the least-privileged role. `Owner = 1` represents project creators or administrators. Additional roles (e.g., `Viewer`, `Maintainer`) can be added in future iterations without schema changes — EF Core stores the enum as an `int` column.

### 2. `Project` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
namespace KanbAI_Core.Models.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- `Name` uses `string.Empty` default to avoid null-reference issues while still requiring a value at the database level via EF Core configuration.
- `Members` is the navigation collection for the one-to-many side of the `Project → ProjectMember` relationship. Initialized to `new List<ProjectMember>()` to prevent null-reference when iterating before EF loads related data.
- The GitHub issue comment includes `Description` (NVARCHAR(MAX)) and `AdminId` (FK to Users) columns. These are **excluded from this implementation** because the acceptance criteria only require a `Name` property, and the `Owner` role in `ProjectMember` replaces the need for a dedicated `AdminId` FK. These can be added in a future issue if needed.

### 3. `ProjectMember` Junction Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/ProjectMember.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Models.Entities;

public class ProjectMember : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ProjectRole Role { get; set; }
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- Inherits from `BaseEntity` per AC #4, giving it a surrogate `Id` (Guid) primary key rather than a composite PK on `(ProjectId, UserId)`. The composite uniqueness is enforced via a unique index instead.
- `null!` on navigation properties is the standard EF Core pattern — EF populates these via lazy/eager loading; the null-forgiving operator suppresses compiler warnings without masking real nullability issues.
- `Role` defaults to `ProjectRole.Member` (enum default = 0), reinforced by the EF configuration's `HasDefaultValue`.
- The GitHub issue comment shows a composite PK `(ProjectId, UserId)`, but since `ProjectMember` inherits `BaseEntity` (which provides a `Guid Id`), the surrogate PK is used instead with a unique constraint on `(ProjectId, UserId)` to enforce the "no duplicate membership" business rule.

### 4. `User` Entity Update — Navigation Property

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

Add the following navigation property to satisfy AC #9 (navigation from User to projects):

```csharp
public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
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
}
```

### 5. EF Core Entity Configuration — `ProjectConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/ProjectConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);
    }
}
```

**Constraint Rationale:**

| Property | Constraint | Reason |
|----------|------------|--------|
| `Name` | Required, MaxLength(200) | Project name cannot be blank; 200 chars accommodates descriptive project titles. |

**Note:** The `Project → Members` relationship is configured from the `ProjectMemberConfiguration` side (the dependent entity owns the FK), keeping relationship configuration in one place and avoiding duplication.

### 6. EF Core Entity Configuration — `ProjectMemberConfiguration`

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/ProjectMemberConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.HasKey(pm => pm.Id);

        builder.HasIndex(pm => new { pm.ProjectId, pm.UserId })
            .IsUnique();

        builder.Property(pm => pm.Role)
            .IsRequired()
            .HasDefaultValue(ProjectRole.Member);

        builder.HasOne(pm => pm.Project)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pm => pm.User)
            .WithMany(u => u.ProjectMemberships)
            .HasForeignKey(pm => pm.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

**Constraint Rationale:**

| Property / Constraint | Type | Reason |
|-----------------------|------|--------|
| `Id` | Primary Key | Surrogate PK inherited from `BaseEntity`. |
| `(ProjectId, UserId)` | Unique Index | Enforces AC #11 — a user cannot be a member of the same project more than once. |
| `Role` | Required, Default `Member` | Every membership must have a role. Default to least privilege. |
| `ProjectId` FK | Cascade delete | Deleting a project removes all its memberships. |
| `UserId` FK | Restrict delete | Prevents orphaned data; a user must have their memberships removed before account deletion. This also avoids SQL Server's "multiple cascade paths" error (both `Project` and `User` cascading to `ProjectMember` is disallowed by SQL Server). |

### 7. `ApplicationDbContext` Update

**File:** `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`

Add two new `DbSet<>` properties:

```csharp
public DbSet<Project> Projects => Set<Project>();
public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
```

No changes to `OnModelCreating` — `ApplyConfigurationsFromAssembly` already auto-discovers all `IEntityTypeConfiguration<T>` implementations. No changes to `SaveChangesAsync` — timestamp stamping already covers all `BaseEntity` subclasses.

### 8. Migration

**Name:** `AddProjectAndProjectMemberEntities`

The migration will produce two tables:

**`Projects` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `Name` | `nvarchar(200)` | NO | |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**`ProjectMembers` table:**

| Column | SQL Type | Nullable | Constraints |
|--------|----------|----------|-------------|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `ProjectId` | `uniqueidentifier` | NO | FK → Projects(Id), ON DELETE CASCADE |
| `UserId` | `uniqueidentifier` | NO | FK → Users(Id), ON DELETE NO ACTION |
| `Role` | `int` | NO | Default: `0` (Member) |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

**Indexes:**
- `IX_ProjectMembers_ProjectId_UserId` — UNIQUE on `(ProjectId, UserId)`
- `IX_ProjectMembers_UserId` — Non-unique (auto-created by EF Core for FK navigation)

## API Contracts

N/A — no API endpoints for this issue. The entities are domain models only at this stage. API endpoints for project CRUD and membership management will be introduced in subsequent issues.

## Application Layer Boundaries

N/A — no service interfaces, MediatR handlers, or DTOs are required for this issue. Those will be introduced when API endpoints are added.

## Implementation Steps for @agent_developer

Follow these steps strictly in order, referencing `@rule_code_standards`:

1. **Create `ProjectRole` enum:**
   - Create `ProjectRole.cs` in `KanbAI-Core/KanbAI-Core/Models/Enums/`.
   - Define `Member = 0` and `Owner = 1`.
   - Use file-scoped namespace `KanbAI_Core.Models.Enums`.

2. **Create `Project` entity:**
   - Create `Project.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `Name` (string, default `string.Empty`) and `Members` (ICollection\<ProjectMember\>, default `new List<ProjectMember>()`).
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

3. **Create `ProjectMember` entity:**
   - Create `ProjectMember.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `ProjectId` (Guid), `Project` (navigation, `= null!`), `UserId` (Guid), `User` (navigation, `= null!`), `Role` (ProjectRole).
   - Add `using KanbAI_Core.Models.Enums;`.
   - Use file-scoped namespace `KanbAI_Core.Models.Entities`.

4. **Update `User` entity — add navigation property:**
   - Open `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`.
   - Add `public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();` after the `Role` property.

5. **Create `ProjectConfiguration`:**
   - Create `ProjectConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<Project>` with the constraints specified in Section 5 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

6. **Create `ProjectMemberConfiguration`:**
   - Create `ProjectMemberConfiguration.cs` in `KanbAI-Core/KanbAI-Core/Data/Configurations/`.
   - Implement `IEntityTypeConfiguration<ProjectMember>` with all constraints, relationships, and cascade behaviors specified in Section 6 above.
   - Use file-scoped namespace `KanbAI_Core.Data.Configurations`.

7. **Update `ApplicationDbContext`:**
   - Open `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`.
   - Add `public DbSet<Project> Projects => Set<Project>();`.
   - Add `public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();`.
   - Add necessary `using` statements if not already present.

8. **Generate EF Core migration:**
   - From the `KanbAI-Core/KanbAI-Core` project directory, run:
     ```
     dotnet ef migrations add AddProjectAndProjectMemberEntities
     ```
   - If `dotnet ef` fails due to WDAC policy, a `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` — it should resolve the issue.
   - Verify the generated migration creates both tables with all expected columns, constraints, FKs, and indexes per the schema tables in Section 8 above.

9. **Build and verify:**
   - Run `dotnet build` from the solution directory to ensure the solution compiles without errors.
   - Run `dotnet test` to confirm all existing tests continue to pass.

## QA Guidance for @agent_tester_qa

### Unit Tests Required

**File:** `KanbAI-Core.Tests/Models/Entities/ProjectTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `Project_InheritsFromBaseEntity` | Happy Path | Verify `typeof(Project).IsSubclassOf(typeof(BaseEntity))` |
| `Project_NameProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `Project_MembersProperty_IsCollectionOfProjectMember` | Happy Path | Verify the `Members` property is `ICollection<ProjectMember>` |
| `Project_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify `Name` and `Members` + inherited are publicly accessible |
| `Project_DefaultPropertyValues_AreExpected` | Edge Case | New `Project()` should have `string.Empty` for Name, empty list for Members |

**File:** `KanbAI-Core.Tests/Models/Entities/ProjectMemberTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ProjectMember_InheritsFromBaseEntity` | Happy Path | Verify `typeof(ProjectMember).IsSubclassOf(typeof(BaseEntity))` |
| `ProjectMember_ProjectIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `ProjectMember_UserIdProperty_IsGuidType` | Happy Path | Verify property type via reflection |
| `ProjectMember_RoleProperty_IsProjectRoleType` | Happy Path | Verify property type via reflection |
| `ProjectMember_ProjectNavigationProperty_Exists` | Happy Path | Verify `Project` navigation property exists and is `Project` type |
| `ProjectMember_UserNavigationProperty_Exists` | Happy Path | Verify `User` navigation property exists and is `User` type |
| `ProjectMember_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all properties are publicly accessible |
| `ProjectMember_DefaultRoleValue_IsMember` | Edge Case | New `ProjectMember()` should have `ProjectRole.Member` for Role |

**File:** `KanbAI-Core.Tests/Models/Enums/ProjectRoleTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ProjectRole_MemberValue_IsZero` | Happy Path | `(int)ProjectRole.Member` should equal `0` |
| `ProjectRole_OwnerValue_IsOne` | Happy Path | `(int)ProjectRole.Owner` should equal `1` |
| `ProjectRole_DefaultValue_IsMember` | Edge Case | `default(ProjectRole)` should equal `ProjectRole.Member` |
| `ProjectRole_IsEnum` | Happy Path | `typeof(ProjectRole).IsEnum` should be true |

**File:** `KanbAI-Core.Tests/Data/Configurations/ProjectConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ProjectConfiguration_Name_IsRequired` | Happy Path | A project with null/empty name should fail on `SaveChangesAsync` |
| `ProjectConfiguration_Name_MaxLength200_Accepted` | Happy Path | A project with 200-char name should save successfully |

**File:** `KanbAI-Core.Tests/Data/Configurations/ProjectMemberConfigurationTests.cs` (using SQLite in-memory)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ProjectMemberConfiguration_UniqueConstraint_PreventsDoubleMembership` | Happy Path | Adding the same user to the same project twice should throw on `SaveChangesAsync` |
| `ProjectMemberConfiguration_Role_DefaultsToMember` | Edge Case | A project member saved without explicit role should have `Member` after retrieval |
| `ProjectMemberConfiguration_CascadeDelete_RemovesMembersWhenProjectDeleted` | Happy Path | Deleting a project should cascade-delete its `ProjectMember` records |
| `ProjectMemberConfiguration_RestrictDelete_PreventsUserDeletionWithMemberships` | Happy Path | Deleting a user who has memberships should throw |
| `ProjectMemberConfiguration_DifferentUsersCanJoinSameProject` | Happy Path | Two different users joining the same project should succeed |
| `ProjectMemberConfiguration_SameUserCanJoinDifferentProjects` | Happy Path | The same user joining two different projects should succeed |

**File:** `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `ApplicationDbContext_ProjectsDbSet_IsNotNull` | Happy Path | Verify `context.Projects` returns a non-null `DbSet<Project>` |
| `ApplicationDbContext_ProjectMembersDbSet_IsNotNull` | Happy Path | Verify `context.ProjectMembers` returns a non-null `DbSet<ProjectMember>` |

**File:** `KanbAI-Core.Tests/Models/Entities/UserTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `User_ProjectMembershipsProperty_IsCollectionOfProjectMember` | Happy Path | Verify the new navigation property exists and is `ICollection<ProjectMember>` |

### Test Infrastructure Notes
- Use `SQLite in-memory` for configuration tests that validate constraints (unique indexes, required fields, cascade behavior). The InMemory provider does not enforce these.
- Use `InMemoryDatabase` for simple entity/DbSet existence tests (consistent with existing patterns).
- Follow the `MethodName_StateUnderTest_ExpectedBehavior` naming convention per `@rule_testing_observability`.
- Follow the AAA pattern with blank-line separators per existing test conventions.
- No integration tests (`WebApplicationFactory`) are required for this issue as there are no API endpoints.

## Known Caveats

| Caveat | Impact | Mitigation |
|--------|--------|------------|
| **SQL Server multiple cascade paths** | SQL Server disallows multiple cascade delete paths to the same table. Both `Project` and `User` have FKs to `ProjectMember`. | `User → ProjectMember` is set to `Restrict` (not Cascade). Application logic must handle membership cleanup before user deletion. |
| **InMemory provider limitations** | InMemory does not enforce unique indexes, FK constraints, or cascade behaviors. | Use SQLite in-memory for configuration tests that validate constraints. |
| **SQLite max-length limitations** | SQLite does not enforce `nvarchar(N)` max lengths. | Max-length validation (e.g., Name > 200 chars) requires SQL Server integration tests, which are out of scope for this issue. |
| **GitHub comment schema discrepancy** | The issue comment's SQL schema includes `Title`, `Description`, and `AdminId` columns not present in the acceptance criteria. | The AC is authoritative. Only `Name` is implemented. `Description` and admin ownership via `AdminId` can be added in future issues; `Owner` role in `ProjectRole` covers the admin use case. |
| **WDAC policy** | `dotnet ef` may fail on host startup due to Windows WDAC policy blocking `Scalar.AspNetCore.dll`. | A `DesignTimeDbContextFactory` already exists in `Data/DesignTimeDbContextFactory.cs` and should resolve this. |

## Development Status

**Status:** Complete — implemented 2026-04-11.

### Files Created

| File Path | Purpose |
|-----------|---------|
| `KanbAI-Core/KanbAI-Core/Models/Enums/ProjectRole.cs` | `ProjectRole` enum with `Member = 0`, `Owner = 1` |
| `KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs` | `Project` entity inheriting `BaseEntity` with `Name` and `Members` navigation |
| `KanbAI-Core/KanbAI-Core/Models/Entities/ProjectMember.cs` | `ProjectMember` junction entity with `ProjectId`, `UserId`, `Role`, and navigation properties |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/ProjectConfiguration.cs` | EF Core fluent config for `Project` (PK, Name required/MaxLength 200) |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/ProjectMemberConfiguration.cs` | EF Core fluent config for `ProjectMember` (PK, unique index, FK cascade/restrict, default role) |
| `KanbAI-Core/KanbAI-Core/Migrations/20260411100602_AddProjectAndProjectMemberEntities.cs` | EF Core migration creating `Projects` and `ProjectMembers` tables |
| `KanbAI-Core/KanbAI-Core/Migrations/20260411100602_AddProjectAndProjectMemberEntities.Designer.cs` | Migration designer file (auto-generated) |

### Files Modified

| File Path | Change |
|-----------|--------|
| `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs` | Added `ProjectMemberships` navigation property (`ICollection<ProjectMember>`) |
| `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs` | Added `DbSet<Project> Projects` and `DbSet<ProjectMember> ProjectMembers` |
| `KanbAI-Core/KanbAI-Core/Migrations/ApplicationDbContextModelSnapshot.cs` | Updated snapshot with new entities (auto-generated) |

### Build & Test Results

- **Build:** SUCCESS — 0 errors, 0 warnings
- **Tests:** 111 total, 109 passed, 0 failed, 2 skipped (pre-existing skips)

### Infrastructure Notes

- No additional infrastructure or workarounds were required. The existing `DesignTimeDbContextFactory` resolved the design-time context for migration generation.
- An initial migration generation attempt failed because `KanbAI-Core.exe` was locked by a running process (PID 5088). The process was stopped and migration generated successfully on retry.

### Edge Cases for QA

- **Unique constraint enforcement:** The `IX_ProjectMembers_ProjectId_UserId` unique index prevents duplicate memberships. This cannot be validated with InMemory provider — use SQLite in-memory for this test.
- **Cascade delete:** Deleting a `Project` cascades to `ProjectMember`. Verify orphan records are removed.
- **Restrict delete:** Deleting a `User` with active memberships should fail (Restrict behavior). This prevents orphaned `ProjectMember` records.
- **Default role:** `ProjectRole.Member` (0) is the database default. A `ProjectMember` saved without an explicit role should retrieve as `Member`.
- **SQLite limitation:** SQLite does not enforce `nvarchar(N)` max lengths, so max-length validation for `Name > 200 chars` requires SQL Server integration tests (out of scope).

## Testing Status

**Status:** Complete — all tests passing (2026-04-11).

### Test Summary

| Metric | Count |
|--------|-------|
| Total Tests | 139 |
| Passed | 137 |
| Failed | 0 |
| Skipped | 2 (pre-existing) |

### New Tests Added (28 tests across 7 files)

**`KanbAI-Core.Tests/Models/Enums/ProjectRoleTests.cs`** (4 tests)

| Test Name | Status |
|-----------|--------|
| `ProjectRole_MemberValue_IsZero` | PASS |
| `ProjectRole_OwnerValue_IsOne` | PASS |
| `ProjectRole_DefaultValue_IsMember` | PASS |
| `ProjectRole_IsEnum` | PASS |

**`KanbAI-Core.Tests/Models/Entities/ProjectTests.cs`** (5 tests)

| Test Name | Status |
|-----------|--------|
| `Project_InheritsFromBaseEntity` | PASS |
| `Project_NameProperty_IsStringType` | PASS |
| `Project_MembersProperty_IsCollectionOfProjectMember` | PASS |
| `Project_AllProperties_HavePublicGettersAndSetters` | PASS |
| `Project_DefaultPropertyValues_AreExpected` | PASS |

**`KanbAI-Core.Tests/Models/Entities/ProjectMemberTests.cs`** (8 tests)

| Test Name | Status |
|-----------|--------|
| `ProjectMember_InheritsFromBaseEntity` | PASS |
| `ProjectMember_ProjectIdProperty_IsGuidType` | PASS |
| `ProjectMember_UserIdProperty_IsGuidType` | PASS |
| `ProjectMember_RoleProperty_IsProjectRoleType` | PASS |
| `ProjectMember_ProjectNavigationProperty_Exists` | PASS |
| `ProjectMember_UserNavigationProperty_Exists` | PASS |
| `ProjectMember_AllProperties_HavePublicGettersAndSetters` | PASS |
| `ProjectMember_DefaultRoleValue_IsMember` | PASS |

**`KanbAI-Core.Tests/Data/Configurations/ProjectConfigurationTests.cs`** (2 tests)

| Test Name | Status |
|-----------|--------|
| `ProjectConfiguration_Name_IsRequired` | PASS |
| `ProjectConfiguration_Name_MaxLength200_Accepted` | PASS |

**`KanbAI-Core.Tests/Data/Configurations/ProjectMemberConfigurationTests.cs`** (6 tests)

| Test Name | Status |
|-----------|--------|
| `ProjectMemberConfiguration_UniqueConstraint_PreventsDoubleMembership` | PASS |
| `ProjectMemberConfiguration_Role_DefaultsToMember` | PASS |
| `ProjectMemberConfiguration_CascadeDelete_RemovesMembersWhenProjectDeleted` | PASS |
| `ProjectMemberConfiguration_RestrictDelete_PreventsUserDeletionWithMemberships` | PASS |
| `ProjectMemberConfiguration_DifferentUsersCanJoinSameProject` | PASS |
| `ProjectMemberConfiguration_SameUserCanJoinDifferentProjects` | PASS |

**`KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs`** (2 tests added to existing file)

| Test Name | Status |
|-----------|--------|
| `ApplicationDbContext_ProjectsDbSet_IsNotNull` | PASS |
| `ApplicationDbContext_ProjectMembersDbSet_IsNotNull` | PASS |

**`KanbAI-Core.Tests/Models/Entities/UserTests.cs`** (1 test added to existing file)

| Test Name | Status |
|-----------|--------|
| `User_ProjectMembershipsProperty_IsCollectionOfProjectMember` | PASS |

### Coverage Notes

- **Unit tests** cover entity shape, inheritance, property types, defaults, and public accessors.
- **Configuration tests** use SQLite in-memory to enforce real constraint validation (unique indexes, required fields, cascade/restrict delete behavior).
- **InMemory provider** is used only for simple DbSet existence checks (consistent with existing patterns).
- **Not covered (by design):** `Name` max-length enforcement (SQLite does not enforce `nvarchar(N)` limits — would require SQL Server integration tests, out of scope for this issue).
